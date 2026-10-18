using CommunityToolkit.Mvvm.ComponentModel;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Domain.Scheduling;
using FuShi.Infrastructure;

namespace FuShi.ViewModels;

/// <summary>
/// 悬浮组件的视图模型：承载「科目 / 倒计时 / 进度条 / 下一节」四要素的展示状态与设置读写。
/// 不依赖任何 WPF 类型（纯 net8.0），计时由视图层的 DispatcherTimer 驱动 <see cref="Refresh"/>，
/// 因此可脱离 UI 单测。
/// <para>
/// 数据流：宿主（或引擎的「数据变更」事件）把已展开的时段表经 <see cref="ApplySchedule"/> 推入，
/// 之后每秒的 <see cref="Refresh"/> 只在这份缓存上做纯计算（取景 + 文案），<b>不打数据库</b>。
/// </para>
/// </summary>
public partial class WidgetViewModel : ObservableObject
{
    private readonly AppSettingRepository _settingsRepository;
    private readonly AppSetting _settings;

    /// <summary>缓存的时段表（升序）；由宿主推送，刷新时只读。</summary>
    private IReadOnlyList<TimelineSlot> _slots = Array.Empty<TimelineSlot>();

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
    /// 推入新的时段表（升序）。由宿主在启动时、以及引擎报告数据变更时调用。
    /// 推入后立即按当前时刻重算一次，使界面即时反映变化。
    /// </summary>
    /// <param name="slots">已展开的时段表（升序）。</param>
    public void ApplySchedule(IReadOnlyList<TimelineSlot> slots)
    {
        _slots = slots ?? Array.Empty<TimelineSlot>();
        Refresh(DateTime.Now);
    }

    /// <summary>
    /// 按当前时刻刷新展示状态（每秒由视图调用）。做两件事：更新日期文本；
    /// 在缓存的时段表上取景，算出四要素。全过程纯计算，无 IO。
    /// </summary>
    /// <param name="now">当前时刻（由调用方传入，便于测试与控制）。</param>
    public void Refresh(DateTime now)
    {
        UpdateDateText(now);

        var view = ScheduleTimeline.Select(_slots, now);
        var focus = view.Current ?? view.Next; // 主位：当前没有正在上的课，就聚焦「下一节」。

        if (focus is null)
        {
            ApplyEmptyState();
            return;
        }

        SubjectText = focus.SubjectName;

        if (view.Current is not null)
        {
            // 正在上课：倒计时是「还剩多久」，进度条是本节完成度。
            HasCountdown = true;
            CountdownText = string.Format(WidgetText.COUNTDOWN_REMAINING_FORMAT, FormatDuration(view.Current.EndsAt - now));
            HasProgress = true;
            ProgressPercent = ScheduleTimeline.GetProgressPercent(view.Current, now);
        }
        else
        {
            // 尚未开课：倒计时是「距开始还有多久」，不展示进度条。
            HasCountdown = true;
            CountdownText = string.Format(WidgetText.COUNTDOWN_UNTIL_START_FORMAT, FormatDuration(view.Next!.StartsAt - now));
            HasProgress = false;
            ProgressPercent = 0;
        }

        NextText = BuildNextText(view, now);
        ExpandedTitle = focus.SubjectName;
        ExpandedBody = BuildExpandedBody(now);
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

    /// <summary>更新日期文本（如「10月18日 周六」）。</summary>
    private void UpdateDateText(DateTime now)
    {
        var weekday = WidgetText.WEEKDAY_LABELS[(int)now.DayOfWeek];
        DateText = $"{now.ToString(WidgetText.DATE_FORMAT)} 周{weekday}";
    }

    /// <summary>回到空课表态：四要素全部收起或退化为引导文案。</summary>
    private void ApplyEmptyState()
    {
        SubjectText = WidgetText.EMPTY_SUBJECT;
        HasCountdown = false;
        CountdownText = string.Empty;
        HasProgress = false;
        ProgressPercent = 0;
        NextText = WidgetText.EMPTY_NEXT;
        ExpandedTitle = WidgetText.EMPTY_SUBJECT;
        ExpandedBody = WidgetText.EMPTY_EXPANDED_BODY;
    }

    /// <summary>
    /// 「下一节」文案。主位已经占用「当前或下一节」，因此这里描述的是更靠后的那一节
    /// （当前有课时看 <see cref="TimelineView.Next"/>，当前无课时看 <see cref="TimelineView.Following"/>）。
    /// </summary>
    private static string BuildNextText(TimelineView view, DateTime now)
    {
        var upcoming = view.Current is not null ? view.Next : view.Following;
        if (upcoming is null)
        {
            return WidgetText.LAST_OF_DAY_TEXT;
        }

        var time = upcoming.StartsAt.ToString(WidgetText.CLOCK_FORMAT);
        var day = DateOnly.FromDateTime(upcoming.StartsAt);
        var today = DateOnly.FromDateTime(now);

        if (day == today)
        {
            return string.Format(WidgetText.NEXT_TODAY_FORMAT, time, upcoming.SubjectName);
        }

        if (day == today.AddDays(1))
        {
            return string.Format(WidgetText.NEXT_TOMORROW_FORMAT, time, upcoming.SubjectName);
        }

        var weekday = WidgetText.WEEKDAY_LABELS[(int)upcoming.StartsAt.DayOfWeek];
        return string.Format(WidgetText.NEXT_LATER_FORMAT, weekday, time, upcoming.SubjectName);
    }

    /// <summary>展开态正文：今天剩余时段的紧凑列举；今天已无课时退回空态说明。</summary>
    private string BuildExpandedBody(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var items = _slots
            .Where(slot => DateOnly.FromDateTime(slot.StartsAt) == today && slot.EndsAt > now)
            .Select(slot => string.Format(WidgetText.EXPANDED_ITEM_FORMAT, slot.StartsAt.ToString(WidgetText.CLOCK_FORMAT), slot.SubjectName))
            .ToList();

        if (items.Count == 0)
        {
            return WidgetText.EMPTY_EXPANDED_BODY;
        }

        return string.Format(WidgetText.EXPANDED_BODY_FORMAT, string.Join(WidgetText.EXPANDED_ITEM_SEPARATOR, items));
    }

    /// <summary>把时长格式化成中文短文案（≥1 小时走「N小时MM分」，否则「M分SS秒」，负数归零）。</summary>
    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? string.Format(WidgetText.DURATION_HOURS_FORMAT, (int)span.TotalHours, span.Minutes)
            : string.Format(WidgetText.DURATION_MINUTES_FORMAT, span.Minutes, span.Seconds);
    }
}
