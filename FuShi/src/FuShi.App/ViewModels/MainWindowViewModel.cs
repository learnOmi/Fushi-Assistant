using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Domain.Scheduling;
using FuShi.Infrastructure.Scheduling;
using Serilog;

namespace FuShi.ViewModels;

/// <summary>
/// 主窗口视图模型：承载「课表」页的状态与命令——阶段自举、周网格加载、编辑/新建/删除时段。
/// <para>
/// 唯一收口方法 <see cref="AfterScheduleChangedAsync"/>：任何一次课表改动落库后都要走这里
/// （对账 → 唤醒引擎 → 刷新悬浮组件 → 重载网格），避免在每个命令里各写一遍。
/// </para>
/// 对话框以委托注入（<see cref="Func{T,TResult}"/>），视图负责真正弹窗——UI 的偶然行为留在视图层。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ScheduleEditService _editService;
    private readonly ScheduleService _scheduleService;
    private readonly PhaseService _phaseService;
    private readonly ReminderEngine _engine;
    private readonly SettingsViewModel _settings;
    private readonly Action _reloadWidget;
    private readonly Func<EntryEditorViewModel, bool> _showEntryEditor;
    private readonly Func<SubjectEditorViewModel, bool> _showSubjectEditor;
    private readonly Func<PhaseManagerViewModel, bool> _showPhaseManager;
    private readonly ILogger _logger;

    private Phase? _phase;

    /// <summary>阶段下拉项：主键、名称、是否生效。</summary>
    public sealed record PhaseOption(int Id, string Name, bool IsActive);

    /// <summary>阶段名显示文本。</summary>
    [ObservableProperty]
    private string _phaseText = string.Empty;

    /// <summary>阶段下拉的全部选项（含当前生效标记）。</summary>
    [ObservableProperty]
    private IReadOnlyList<PhaseOption> _phaseOptions = Array.Empty<PhaseOption>();

    /// <summary>当前周网格。</summary>
    [ObservableProperty]
    private WeekGridViewModel? _grid;

    /// <summary>构造。</summary>
    /// <param name="editService">课表写服务。</param>
    /// <param name="scheduleService">课表读服务。</param>
    /// <param name="phaseService">阶段管理服务。</param>
    /// <param name="engine">提醒引擎（改动后请求重排）。</param>
    /// <param name="settings">设置页视图模型（供设置页签绑定）。</param>
    /// <param name="reloadWidget">刷新悬浮组件的回调（由宿主提供）。</param>
    /// <param name="showEntryEditor">弹出时段编辑对话框的委托。</param>
    /// <param name="showSubjectEditor">弹出新建科目对话框的委托。</param>
    /// <param name="showPhaseManager">弹出阶段管理对话框的委托。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public MainWindowViewModel(
        ScheduleEditService editService,
        ScheduleService scheduleService,
        PhaseService phaseService,
        ReminderEngine engine,
        SettingsViewModel settings,
        Action reloadWidget,
        Func<EntryEditorViewModel, bool> showEntryEditor,
        Func<SubjectEditorViewModel, bool> showSubjectEditor,
        Func<PhaseManagerViewModel, bool> showPhaseManager,
        ILogger logger)
    {
        _editService = editService;
        _scheduleService = scheduleService;
        _phaseService = phaseService;
        _engine = engine;
        _settings = settings;
        _reloadWidget = reloadWidget;
        _showEntryEditor = showEntryEditor;
        _showSubjectEditor = showSubjectEditor;
        _showPhaseManager = showPhaseManager;
        _logger = logger;
    }

    /// <summary>设置页视图模型（设置页签的数据上下文）。</summary>
    public SettingsViewModel Settings => _settings;

    /// <summary>是否空课表（驱动空态提示）。</summary>
    public bool IsEmpty => Grid?.IsEmpty ?? true;

    /// <summary>
    /// 载入：自举生效阶段（无则创建「默认课表」）→ 取周网格模板 → 投影。
    /// 每次窗口显示时调用，保证数据最新。
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _phase = await _editService.EnsureActivePhaseAsync();
            PhaseText = string.Format(WindowText.PHASE_FORMAT, _phase.Name);
            await ReloadPhaseOptionsAsync();
            await ReloadGridAsync();
            await _settings.LoadAsync();
        }
        catch (Exception ex)
        {
            // 载入失败不吞异常：记日志后保留空态，避免 async void 事件处理器把异常抛成进程崩溃。
            _logger.Error(ex, "加载主窗口课表失败");
        }
    }

    /// <summary>载入阶段下拉选项（含生效标记）。</summary>
    private async Task ReloadPhaseOptionsAsync()
    {
        var phases = await _phaseService.GetPhasesAsync();
        PhaseOptions = phases
            .Select(static p => new PhaseOption(p.Id, p.Name, p.IsActive))
            .ToList();
    }

    /// <summary>
    /// 切换生效阶段（阶段下拉里选中某一项）。
    /// 与同一阶段重复点击时直接返回，避免无意义的全量重排。
    /// </summary>
    /// <param name="phaseId">目标阶段主键。</param>
    [RelayCommand]
    private async Task SwitchPhaseAsync(int phaseId)
    {
        try
        {
            if (_phase?.Id == phaseId)
            {
                return;
            }

            if (!await _phaseService.ActivateAsync(phaseId))
            {
                // 阶段已被并发删除：只回读下拉，不打断当前课表。
                await ReloadPhaseOptionsAsync();
                return;
            }

            await AfterPhaseChangedAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "切换阶段失败");
            await ReloadPhaseOptionsAsync();
        }
    }

    /// <summary>打开阶段管理对话框；对话框报告改过才做切换收口。</summary>
    [RelayCommand]
    private async Task OpenPhaseManagerAsync()
    {
        try
        {
            var managerVm = new PhaseManagerViewModel(_phaseService, _logger);
            if (!_showPhaseManager(managerVm))
            {
                return;
            }

            await AfterPhaseChangedAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开阶段管理失败");
            await ReloadPhaseOptionsAsync();
        }
    }

    /// <summary>
    /// 阶段变更后的统一收口：回读生效阶段 → 刷新下拉与徽标 → <b>先</b>全量对账删旧实例并展开新阶段
    /// → <b>再</b>唤醒引擎重排 → 刷新悬浮组件 → 重载网格。
    /// 顺序不能反：若先 <c>RequestRescan</c>，引擎可能在旧实例尚未清除时先扫到旧阶段的提醒并弹出来。
    /// </summary>
    private async Task AfterPhaseChangedAsync()
    {
        _phase = await _editService.EnsureActivePhaseAsync();
        PhaseText = string.Format(WindowText.PHASE_FORMAT, _phase.Name);

        await ReloadPhaseOptionsAsync();
        await _scheduleService.ReconcileAllAsync(DateTime.Now);
        _engine.RequestRescan();
        _reloadWidget();
        await ReloadGridAsync();
    }

    /// <summary>「添加时段」：以"今天 + 下一刻（向上取整）"预填一条新时段。</summary>
    [RelayCommand]
    private async Task AddEntryAsync()
    {
        if (_phase is null)
        {
            return;
        }

        var now = DateTime.Now;
        var start = RoundUpToStep(TimeOnly.FromDateTime(now));

        var editorVm = new EntryEditorViewModel(_editService, _showSubjectEditor);
        editorVm.InitializeNew(_phase.Id, now.DayOfWeek, start);

        if (!_showEntryEditor(editorVm))
        {
            return;
        }

        if (editorVm.SavedEntry is { } saved)
        {
            await AfterScheduleChangedAsync(new[] { saved.Id });
        }
    }

    /// <summary>重新投影周网格（含「今天」已顺延实例的叠加以供角标）。</summary>
    private async Task ReloadGridAsync()
    {
        if (_phase is null)
        {
            return;
        }

        var now = DateTime.Now;
        var sources = await _scheduleService.GetWeekGridAsync(_phase.Id, now);
        var grid = WeekGridProjector.Project(sources, now);
        Grid = new WeekGridViewModel(grid, OnCellClicked, now.DayOfWeek);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// 供宿主在「实例级」变更（如弹窗点「稍后」顺延）后刷新周网格。
    /// 这类变更不经过课表编辑命令，故不触发 <see cref="AfterScheduleChangedAsync"/>；
    /// 内部捕获异常并记日志，避免 fire-and-forget 的未观察异常。
    /// </summary>
    public async Task RefreshGridAsync()
    {
        try
        {
            await ReloadGridAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "刷新主窗口周网格失败");
        }
    }

    /// <summary>
    /// 单元格点击：空格子 → 以该天该时刻预填新建；有格子 → 编辑既有条目。
    /// 回调为 async void：UI 事件性质，内部捕获异常并记日志，避免未观察异常。
    /// </summary>
    /// <param name="cell">被点击的单元格。</param>
    private async void OnCellClicked(WeekGridCellViewModel cell)
    {
        try
        {
            if (_phase is null)
            {
                return;
            }

            var editorVm = new EntryEditorViewModel(_editService, _showSubjectEditor);

            if (cell.EntryId != 0)
            {
                var entry = await _editService.GetEntryAsync(cell.EntryId);
                if (entry is null)
                {
                    return; // 条目已被删除（并发），静默忽略
                }

                editorVm.InitializeExisting(_phase.Id, entry);
            }
            else
            {
                editorVm.InitializeNew(_phase.Id, cell.DayOfWeek, cell.StartTime);
            }

            if (!_showEntryEditor(editorVm))
            {
                return;
            }

            await ApplyEditorResultAsync(editorVm);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理单元格点击时异常");
        }
    }

    /// <summary>把编辑对话框的结果落到调度链上（保存或删除都会触发对账）。</summary>
    /// <param name="editorVm">编辑对话框视图模型。</param>
    private async Task ApplyEditorResultAsync(EntryEditorViewModel editorVm)
    {
        if (editorVm.Deleted)
        {
            await AfterScheduleChangedAsync(new[] { editorVm.EntryId });
            return;
        }

        if (editorVm.SavedEntry is { } saved)
        {
            await AfterScheduleChangedAsync(new[] { saved.Id });
        }
    }

    /// <summary>
    /// 统一收口：课表改动后 → 对账（删未来 Pending 并重展开）→ 唤醒引擎重排 → 刷新悬浮组件 → 重载网格。
    /// 一处收口，命令里不再各写一遍。
    /// </summary>
    /// <param name="entryIds">本次改动涉及的条目主键集合。</param>
    private async Task AfterScheduleChangedAsync(IReadOnlyList<int> entryIds)
    {
        await _scheduleService.ReconcileAsync(entryIds, DateTime.Now);
        _engine.RequestRescan();
        _reloadWidget();
        await ReloadGridAsync();
    }

    /// <summary>把时刻向上取整到编辑步长（如 19:43 → 19:45）。</summary>
    private static TimeOnly RoundUpToStep(TimeOnly time)
    {
        var minutes = time.Hour * 60 + time.Minute;
        var rounded = (minutes + UiConstants.EDITOR_TIME_ROUND_MINUTES - 1) / UiConstants.EDITOR_TIME_ROUND_MINUTES
                      * UiConstants.EDITOR_TIME_ROUND_MINUTES;

        // 取整后可能跨到 24:00；回绕到当天最后一刻，避免越界。
        if (rounded >= 24 * 60)
        {
            rounded = 24 * 60 - UiConstants.EDITOR_TIME_ROUND_MINUTES;
        }

        return new TimeOnly(rounded / 60, rounded % 60);
    }
}
