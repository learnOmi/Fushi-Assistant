using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Time;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Platform;
using FuShi.Infrastructure.Scheduling;
using Serilog;

namespace FuShi.ViewModels;

/// <summary>
/// 设置页视图模型（M3b）：不透明度 / 免打扰 / 提前提醒 / 时区 / 主题的读写与即时生效。
/// <para>
/// 持久化走 <see cref="AppSettingRepository"/> 的单行表；即时生效经注入的回调完成：
/// 不透明度 → <c>applyWidgetOpacity</c>（宿主设悬浮窗透明度，实时预览）；
/// 时区 / 提前量 → 保存后由宿主 <c>requestRescan</c> 重排调度并 <c>reloadWidget</c> 刷新悬浮组件。
/// </para>
/// 沿用既有约束：仓储内部 <c>ConfigureAwait(false)</c>，故 UI 线程可用 <c>GetAwaiter().GetResult()</c>
/// 同步等待一次微小写入而不死锁；而 <see cref="LoadAsync"/> 的 await 不切断上下文，续体留在 UI 线程。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>提前提醒下拉项（分钟值 + 中文标签）。</summary>
    public sealed record LeadOption(int Minutes, string Label);

    /// <summary>主题下拉项（存储值 + 中文标签）。</summary>
    public sealed record ThemeOption(string Value, string Label);

    private readonly AppSettingRepository _repository;
    private readonly ReminderEngine _engine;
    private readonly AutostartService _autostartService;
    private readonly Action _reloadWidget;
    private readonly Action<double> _applyWidgetOpacity;
    private readonly Action<string> _applyTheme;
    private readonly ILogger _logger;

    /// <summary>当前设置快照：读入后复用，保存时只覆盖本页管辖的字段，其余（位置/自启）原样保留。</summary>
    private AppSetting _setting = new();

    /// <summary>「设置已保存」提示的倒计时；重复保存时取消上一轮，保证提示不会提前熄灭或叠加。</summary>
    private CancellationTokenSource? _savedHintCts;

    /// <summary>悬浮窗不透明度（<c>MIN_OPACITY</c>~<c>MAX_OPACITY</c>）。</summary>
    [ObservableProperty]
    private double _opacity = ModelDefaults.DEFAULT_OPACITY;

    /// <summary>是否启用免打扰。</summary>
    [ObservableProperty]
    private bool _quietEnabled;

    /// <summary>免打扰起始小时。</summary>
    [ObservableProperty]
    private int _quietStartHour = UiConstants.QUIET_DEFAULT_START_HOUR;

    /// <summary>免打扰起始分钟。</summary>
    [ObservableProperty]
    private int _quietStartMinute;

    /// <summary>免打扰结束小时。</summary>
    [ObservableProperty]
    private int _quietEndHour = UiConstants.QUIET_DEFAULT_END_HOUR;

    /// <summary>免打扰结束分钟。</summary>
    [ObservableProperty]
    private int _quietEndMinute = UiConstants.QUIET_DEFAULT_END_MINUTE;

    /// <summary>已选提前提醒档位。</summary>
    [ObservableProperty]
    private LeadOption _selectedLeadOption = null!;

    /// <summary>已选时区 Id（IANA）。</summary>
    [ObservableProperty]
    private string _selectedTimezone = TimeDefaults.DEFAULT_TIMEZONE_ID;

    /// <summary>已选主题。</summary>
    [ObservableProperty]
    private ThemeOption _selectedTheme = null!;

    /// <summary>是否显示「设置已保存」提示（任一字段改动后自动清除）。</summary>
    [ObservableProperty]
    private bool _hasSaved;

    /// <summary>是否开机自启（与注册表 Run 项联动，保存时写注册表）。</summary>
    [ObservableProperty]
    private bool _autostart;

    /// <summary>开机自启写入失败时的错误文本；空串表示无错误。</summary>
    [ObservableProperty]
    private string _autostartError = string.Empty;

    /// <summary>是否展示开机自启错误提示。</summary>
    public bool HasAutostartError => !string.IsNullOrEmpty(AutostartError);

    /// <summary>可选小时（0~23）。</summary>
    public IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, UiConstants.HOURS_PER_DAY).ToList();

    /// <summary>可选分钟（0,5,…,55）。</summary>
    public IReadOnlyList<int> MinuteOptions { get; } = Enumerable
        .Range(0, 60 / UiConstants.MINUTE_STEP)
        .Select(i => i * UiConstants.MINUTE_STEP)
        .ToList();

    /// <summary>提前提醒档位（预设；若库中为非常规值，加载时会动态补齐，避免静默改写）。</summary>
    public List<LeadOption> LeadOptions { get; } = UiConstants.LEAD_TIME_PRESET_MINUTES
        .Select(minutes => new LeadOption(minutes, FormatLead(minutes)))
        .ToList();

    /// <summary>
    /// 可选时区（IANA Id，按系统枚举顺序）。
    /// <para>
    /// 必须是可观察集合：库里的时区可能不在 <see cref="TimeZoneInfo.GetSystemTimeZones"/> 里
    /// （Windows 枚举出来的是 <c>GMT Standard Time</c> 这类 Id，而项目存储的是 <c>Europe/London</c> 这类 IANA Id），
    /// 加载时需要把它补进列表。用普通 List 补项不会通知 ComboBox，选中项就显示为空白。
    /// 默认时区在建列表时就补齐，保证首次绑定（早于 LoadAsync）也能正常选中。
    /// </para>
    /// </summary>
    public ObservableCollection<string> TimezoneOptions { get; } = BuildTimezoneOptions();

    /// <summary>可选主题（切换即刻生效，见 <see cref="OnSelectedThemeChanged"/>）。</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
    {
        new ThemeOption(WindowText.SETTINGS_THEME_DARK, WindowText.SETTINGS_THEME_DARK_LABEL),
        new ThemeOption(WindowText.SETTINGS_THEME_LIGHT, WindowText.SETTINGS_THEME_LIGHT_LABEL),
    };

    /// <summary>不透明度百分比显示文本（如「80%」）。</summary>
    public string OpacityText => string.Format(WindowText.SETTINGS_OPACITY_PERCENT_FORMAT, Opacity * 100);

    /// <summary>构造。</summary>
    /// <param name="repository">设置仓储。</param>
    /// <param name="engine">提醒引擎（保存后请求重排）。</param>
    /// <param name="autostart">开机自启服务（保存开关时写 HKCU Run）。</param>
    /// <param name="reloadWidget">刷新悬浮组件的回调（由宿主提供）。</param>
    /// <param name="applyWidgetOpacity">应用悬浮窗透明度的回调（由宿主设置窗口 Opacity）。</param>
    /// <param name="applyTheme">应用主题的回调（由宿主替换应用级资源字典）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public SettingsViewModel(
        AppSettingRepository repository,
        ReminderEngine engine,
        AutostartService autostart,
        Action reloadWidget,
        Action<double> applyWidgetOpacity,
        Action<string> applyTheme,
        ILogger logger)
    {
        _repository = repository;
        _engine = engine;
        _autostartService = autostart;
        _reloadWidget = reloadWidget;
        _applyWidgetOpacity = applyWidgetOpacity;
        _applyTheme = applyTheme;
        _logger = logger;

        // 下拉默认项先占位，真正的取值由 LoadAsync 回填。
        _selectedLeadOption = LeadOptions[0];
        _selectedTheme = ThemeOptions[0];
    }

    /// <summary>
    /// 载入设置并回填界面。每次窗口显示时调用，保证数据最新。
    /// 注意：此 await 不切断同步上下文，续体留在 UI 线程，随后回填绑定属性是安全的。
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _setting = await _repository.GetAsync();
            ApplySetting(_setting);
            HasSaved = false;
        }
        catch (Exception ex)
        {
            // 加载失败不吞异常：记日志后保留当前（默认）界面值，避免把异常抛进 async void 加载链。
            _logger.Error(ex, "加载设置失败");
        }
    }

    /// <summary>
    /// 保存设置：写回单行表，并触发调度重排与悬浮组件刷新（时区/提前量改动即时生效）。
    /// 免打扰未启用、或起止时刻相同（视为不启用）时清空该时段。
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        try
        {
            // 重读最新单行设置后再写本页管辖的字段：悬浮窗拖动会写位置三列，
            // 若在这里回写窗口打开时的旧快照，会把刚拖动的位置覆盖回去（与 PersistPosition 对称的同一类缺陷）。
            var setting = _repository.GetAsync().GetAwaiter().GetResult();
            var previousAutostart = setting.Autostart;

            setting.Opacity = Opacity;
            setting.LeadTimeMinutes = SelectedLeadOption.Minutes;
            setting.TimezoneId = SelectedTimezone;
            setting.Theme = SelectedTheme.Value;
            setting.Autostart = Autostart;

            if (QuietEnabled && !IsQuietTimeEqual())
            {
                setting.QuietStart = new TimeOnly(QuietStartHour, QuietStartMinute);
                setting.QuietEnd = new TimeOnly(QuietEndHour, QuietEndMinute);
            }
            else
            {
                setting.QuietStart = null;
                setting.QuietEnd = null;
            }

            // UI 线程同步等待一次微小写入（仓储内部 ConfigureAwait(false)，不会死锁）。
            _repository.SaveAsync(setting).GetAwaiter().GetResult();

            // 让内存快照与库保持一致，避免下次保存/加载基于过期值。
            _setting = setting;

            // 开机自启是「库 + 注册表」两处状态：先落库，再写注册表；写失败回滚库与开关，
            // 绝不出现「界面开着、开机不启动」的静默失效（M4d 约定）。
            if (Autostart != previousAutostart || Autostart)
            {
                var result = _autostartService.ApplyForCurrentProcess(Autostart);
                if (!result.Success)
                {
                    RollbackAutostart(setting, previousAutostart, result.Error);
                    return;
                }
            }

            AutostartError = string.Empty;

            // 时区/提前量会影响展开与触发点；无条件重排一次最简且幂等（代价小）。
            _engine.RequestRescan();
            _reloadWidget();

            HasSaved = true;
            ScheduleSavedHintDismiss();
            _logger.Information(
                "设置已保存：不透明度 {Opacity}，提前 {Lead} 分钟，时区 {Timezone}，主题 {Theme}，开机自启 {Autostart}",
                Opacity,
                SelectedLeadOption.Minutes,
                SelectedTimezone,
                SelectedTheme.Value,
                Autostart);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存设置失败");
            HasSaved = false;
        }
    }

    /// <summary>
    /// 注册表写入失败的回滚：把库中的开关改回旧值并重写一次，界面开关同步退回。
    /// 时序上库已先行写入新值，故这里必须再保存一次；两次写入都是单行微操作，代价可忽略。
    /// </summary>
    /// <param name="setting">已写入新值的设置对象。</param>
    /// <param name="previousAutostart">库中原有的自启开关。</param>
    /// <param name="error">注册表写入失败原因。</param>
    private void RollbackAutostart(AppSetting setting, bool previousAutostart, string? error)
    {
        setting.Autostart = previousAutostart;
        _repository.SaveAsync(setting).GetAwaiter().GetResult();
        _setting = setting;
        Autostart = previousAutostart;

        AutostartError = string.Format(WindowText.SETTINGS_AUTOSTART_ERROR_FORMAT, error);
        HasSaved = false;
        _logger.Warning("开机自启设置失败，已回滚为 {Autostart}：{Error}", previousAutostart, error);

        // 同一批保存里其余字段（时区 / 提前量 / 不透明度）已经落库，仍要让它们生效；
        // 回滚只针对自启这一项，不连坐其他设置。
        _engine.RequestRescan();
        _reloadWidget();
    }

    /// <summary>
    /// 安排「设置已保存」提示自动消失。每次保存都重置倒计时，避免连续保存时提示被上一轮提前清掉。
    /// <para>
    /// 用 <see cref="Task.Delay(int, CancellationToken)"/> 而非 <c>DispatcherTimer</c>：
    /// <see cref="Save"/> 在 UI 线程同步执行，await 会捕获 WPF 同步上下文，续体自然回到 UI 线程改属性；
    /// 视图模型因此不必引用 <c>Dispatcher</c>，也便于单测。失败只可能是取消，属正常覆盖，无需记日志。
    /// </para>
    /// </summary>
    private void ScheduleSavedHintDismiss()
    {
        _savedHintCts?.Cancel();
        _savedHintCts?.Dispose();
        _savedHintCts = new CancellationTokenSource();

        _ = DismissSavedHintAsync(_savedHintCts.Token);
    }

    /// <summary>等待提示时长后清除「已保存」；被新一次保存取消时直接退出。</summary>
    /// <param name="token">本轮倒计时的取消令牌。</param>
    private async Task DismissSavedHintAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(UiConstants.SAVED_HINT_DURATION_MILLISECONDS, token);
            HasSaved = false;
        }
        catch (OperationCanceledException)
        {
            // 新一次保存接管了提示，本轮倒计时作废。
        }
    }

    /// <summary>把库中设置回填到界面（含越界兜底）。</summary>
    /// <param name="setting">库中设置。</param>
    private void ApplySetting(AppSetting setting)
    {
        Opacity = Clamp(setting.Opacity, ModelDefaults.MIN_OPACITY, ModelDefaults.MAX_OPACITY);

        QuietEnabled = setting.QuietStart is not null && setting.QuietEnd is not null;
        var start = setting.QuietStart ?? new TimeOnly(UiConstants.QUIET_DEFAULT_START_HOUR, UiConstants.QUIET_DEFAULT_END_MINUTE);
        var end = setting.QuietEnd ?? new TimeOnly(UiConstants.QUIET_DEFAULT_END_HOUR, UiConstants.QUIET_DEFAULT_END_MINUTE);
        QuietStartHour = start.Hour;
        QuietStartMinute = RoundToStep(start.Minute);
        QuietEndHour = end.Hour;
        QuietEndMinute = RoundToStep(end.Minute);

        var lead = setting.LeadTimeMinutes > 0 ? setting.LeadTimeMinutes : ModelDefaults.DEFAULT_LEAD_TIME_MINUTES;
        EnsureLeadOption(lead);
        SelectedLeadOption = LeadOptions.First(option => option.Minutes == lead);

        var timezone = string.IsNullOrWhiteSpace(setting.TimezoneId) ? TimeDefaults.DEFAULT_TIMEZONE_ID : setting.TimezoneId;
        if (!TimezoneOptions.Contains(timezone))
        {
            TimezoneOptions.Insert(0, timezone); // 保留库中原值，避免下拉选不到导致静默改写
        }

        SelectedTimezone = timezone;

        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Value == setting.Theme) ?? ThemeOptions[0];

        Autostart = setting.Autostart;
        AutostartError = string.Empty;
    }

    /// <summary>若提前量不在预设档位内，补齐一个选项并保持升序（保留库中原值）。</summary>
    /// <param name="minutes">库中的提前量分钟数。</param>
    private void EnsureLeadOption(int minutes)
    {
        if (LeadOptions.Any(option => option.Minutes == minutes))
        {
            return;
        }

        LeadOptions.Add(new LeadOption(minutes, FormatLead(minutes)));
        LeadOptions.Sort(static (left, right) => left.Minutes.CompareTo(right.Minutes));
    }

    /// <summary>免打扰起止是否相同（相同视为不启用）。</summary>
    private bool IsQuietTimeEqual() =>
        QuietStartHour == QuietEndHour && QuietStartMinute == QuietEndMinute;

    /// <summary>把分钟向下对齐到下拉步长（保证落在 <see cref="MinuteOptions"/> 内）。</summary>
    private static int RoundToStep(int minute) => minute / UiConstants.MINUTE_STEP * UiConstants.MINUTE_STEP;

    /// <summary>
    /// 构建时区下拉项：由领域层统一构造（系统时区 + 默认 IANA Id 置顶），
    /// 视图模型只负责包成可通知的集合（设计 2.3 ③：Windows 枚举不含 IANA Id）。
    /// </summary>
    private static ObservableCollection<string> BuildTimezoneOptions() =>
        new(TimeZoneOptionBuilder.Build(TimeDefaults.DEFAULT_TIMEZONE_ID));

    /// <summary>把值夹到 [min, max] 闭区间。</summary>
    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>提前量档位的显示文案。</summary>
    private static string FormatLead(int minutes) => string.Format(WindowText.SETTINGS_LEAD_MINUTES_FORMAT, minutes);

    /// <summary>不透明度变化：实时预览悬浮窗 + 刷新百分比文本 + 清除「已保存」提示。</summary>
    partial void OnOpacityChanged(double value)
    {
        _applyWidgetOpacity(value);
        OnPropertyChanged(nameof(OpacityText));
        HasSaved = false;
    }

    partial void OnQuietEnabledChanged(bool value) => HasSaved = false;

    partial void OnQuietStartHourChanged(int value) => HasSaved = false;

    partial void OnQuietStartMinuteChanged(int value) => HasSaved = false;

    partial void OnQuietEndHourChanged(int value) => HasSaved = false;

    partial void OnQuietEndMinuteChanged(int value) => HasSaved = false;

    partial void OnSelectedLeadOptionChanged(LeadOption value) => HasSaved = false;

    partial void OnAutostartChanged(bool value) => HasSaved = false;

    partial void OnAutostartErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasAutostartError));
    }

    partial void OnSelectedTimezoneChanged(string value) => HasSaved = false;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        // 即时预览：与不透明度滑块同构——改下拉立刻换肤，点「保存」才落库。
        // 载入设置回填下拉时也会走到这里，ThemeService 对同值调用是空操作。
        _applyTheme(value.Value);
        HasSaved = false;
    }
}
