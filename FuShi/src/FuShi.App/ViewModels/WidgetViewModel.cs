using CommunityToolkit.Mvvm.ComponentModel;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Infrastructure;

namespace FuShi.ViewModels;

/// <summary>
/// 悬浮组件的视图模型：承载「科目 / 倒计时 / 进度条 / 下一节」四要素的展示状态与设置读写。
/// 不依赖任何 WPF 类型（纯 net8.0），计时由视图层的 DispatcherTimer 驱动 <see cref="Refresh"/>，
/// 因此可脱离 UI 单测。M2a 阶段数据源尚未接入，四要素保持空态；M2b 填充真实课表数据。
/// </summary>
public partial class WidgetViewModel : ObservableObject
{
    private readonly AppSettingRepository _settingsRepository;
    private readonly AppSetting _settings;

    /// <summary>要素① 科目名（或空态文案）。</summary>
    [ObservableProperty]
    private string _subjectText = WidgetText.EMPTY_SUBJECT;

    /// <summary>要素② 倒计时是否可见。</summary>
    [ObservableProperty]
    private bool _hasCountdown;

    /// <summary>要素② 倒计时文本。</summary>
    [ObservableProperty]
    private string _countdownText = string.Empty;

    /// <summary>要素③ 进度条是否可见。</summary>
    [ObservableProperty]
    private bool _hasProgress;

    /// <summary>要素③ 进度百分比（0~100）。</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>要素③ 进度条的剩余百分比（100 - <see cref="ProgressPercent"/>），供星号列权重绑定。</summary>
    public double ProgressRemainderPercent => 100.0 - ProgressPercent;

    /// <summary>进度变化时同步通知剩余百分比（供进度条的剩余列重算）。</summary>
    partial void OnProgressPercentChanged(double value) => OnPropertyChanged(nameof(ProgressRemainderPercent));

    /// <summary>要素④ 下一节文案。</summary>
    [ObservableProperty]
    private string _nextText = WidgetText.EMPTY_NEXT;

    /// <summary>展开态：标题行（当前科目）。</summary>
    [ObservableProperty]
    private string _expandedTitle = WidgetText.EMPTY_SUBJECT;

    /// <summary>展开态：说明正文。</summary>
    [ObservableProperty]
    private string _expandedBody = WidgetText.EMPTY_EXPANDED_BODY;

    /// <summary>展开态：当前是否展开（由视图的悬停逻辑写入，视图据此切换可见性）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>今天的日期文本（如「10月18日 周六」）。</summary>
    [ObservableProperty]
    private string _dateText = string.Empty;

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="settingsRepository">设置仓储（用于持久化窗口位置）。</param>
    /// <param name="settings">启动时加载的单行设置快照。</param>
    public WidgetViewModel(AppSettingRepository settingsRepository, AppSetting settings)
    {
        _settingsRepository = settingsRepository;
        _settings = settings;
        Refresh(DateTime.Now);
    }

    /// <summary>是否存在已保存的窗口位置（false 时按默认右上角摆放）。</summary>
    public bool HasSavedPosition => _settings.HasWidgetPosition;

    /// <summary>已保存的窗口位置 X。</summary>
    public double WidgetX => _settings.WidgetX;

    /// <summary>已保存的窗口位置 Y。</summary>
    public double WidgetY => _settings.WidgetY;

    /// <summary>
    /// 每秒刷新一次展示状态。M2a 只更新日期文本；M2b 将按当前计划实例计算倒计时与进度。
    /// </summary>
    /// <param name="now">当前时刻（由调用方传入，便于测试与控制）。</param>
    public void Refresh(DateTime now)
    {
        var weekday = WidgetText.WEEKDAY_LABELS[(int)now.DayOfWeek];
        DateText = $"{now.ToString(WidgetText.DATE_FORMAT)} 周{weekday}";
    }

    /// <summary>持久化窗口位置（拖动结束时由视图调用）。</summary>
    /// <param name="x">窗口左坐标。</param>
    /// <param name="y">窗口上坐标。</param>
    public void PersistPosition(double x, double y)
    {
        _settings.WidgetX = x;
        _settings.WidgetY = y;
        _settings.HasWidgetPosition = true;

        // 约定：UI 线程上做一次微小的单行写入，用同步等待（仓储内部 ConfigureAwait(false)，不会死锁）。
        _settingsRepository.SaveAsync(_settings).GetAwaiter().GetResult();
    }
}
