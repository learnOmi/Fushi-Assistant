using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Infrastructure.Statistics;
using Serilog;

namespace FuShi.ViewModels;

/// <summary>
/// 统计页的视图模型（M4b）：选区间、调服务聚合、把结果投影成文案与像素比例。
/// <para>
/// 这里<b>不做聚合</b>：完成率、科目占比都在 <see cref="StatisticsService"/> 里算好，
/// 视图模型只负责「百分比 → 文字」「分钟 → 小时」「科目色 → Brush」这类展示适配，
/// 避免同一口径在两处各算一遍而出现数字打架。
/// </para>
/// </summary>
public sealed partial class StatsViewModel : ObservableObject
{
    /// <summary>可选区间。</summary>
    private enum StatsRange
    {
        ThisWeek,
        ThisMonth,
        Custom,
    }

    private readonly StatisticsService _statisticsService;
    private readonly ILogger _logger;

    private StatsRange _range = StatsRange.ThisWeek;
    private DateOnly _from;
    private DateOnly _to;

    /// <summary>区间内是否有记录（决定空态与图表可见性）。</summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>当前区间的可读文案（如 10-19 ~ 10-25）。</summary>
    [ObservableProperty]
    private string _rangeText = string.Empty;

    /// <summary>完成率读数（如 82%）。</summary>
    [ObservableProperty]
    private string _completionRateText = string.Empty;

    /// <summary>已投入时长读数（如 6.5 h）。</summary>
    [ObservableProperty]
    private string _actualHoursText = string.Empty;

    /// <summary>完成次数读数。</summary>
    [ObservableProperty]
    private string _doneCountText = string.Empty;

    /// <summary>完成率卡片的副标题（共 N 次记录）。</summary>
    [ObservableProperty]
    private string _rateSubtitleText = string.Empty;

    /// <summary>时长卡片的副标题（计划 X h）。</summary>
    [ObservableProperty]
    private string _plannedSubtitleText = string.Empty;

    /// <summary>完成次数卡片的副标题（跳过 X · 错过 Y）。</summary>
    [ObservableProperty]
    private string _doneSubtitleText = string.Empty;

    /// <summary>科目分布行。</summary>
    [ObservableProperty]
    private IReadOnlyList<SubjectStatRow> _subjectRows = Array.Empty<SubjectStatRow>();

    /// <summary>最近 7 天柱状条。</summary>
    [ObservableProperty]
    private IReadOnlyList<DayStatRow> _dayRows = Array.Empty<DayStatRow>();

    /// <summary>自定义区间输入框（起始）。</summary>
    [ObservableProperty]
    private string _customFromText = string.Empty;

    /// <summary>自定义区间输入框（结束）。</summary>
    [ObservableProperty]
    private string _customToText = string.Empty;

    /// <summary>校验或加载错误提示（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>没有数据时显示空态。</summary>
    public bool IsEmpty => !HasData;

    /// <summary>是否有错误提示。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>「本周」区间是否选中（RadioButton 双向绑定）。</summary>
    public bool IsThisWeek
    {
        get => _range == StatsRange.ThisWeek;
        set
        {
            if (value)
            {
                SelectRange(StatsRange.ThisWeek);
            }
        }
    }

    /// <summary>「本月」区间是否选中。</summary>
    public bool IsThisMonth
    {
        get => _range == StatsRange.ThisMonth;
        set
        {
            if (value)
            {
                SelectRange(StatsRange.ThisMonth);
            }
        }
    }

    /// <summary>「自定义」区间是否选中；同时决定日期输入行的可见性。</summary>
    public bool IsCustom
    {
        get => _range == StatsRange.Custom;
        set
        {
            if (value)
            {
                SelectRange(StatsRange.Custom);
            }
        }
    }

    /// <summary>构造视图模型，默认选中「本周」。</summary>
    /// <param name="statisticsService">统计聚合服务（只读）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public StatsViewModel(StatisticsService statisticsService, ILogger logger)
    {
        _statisticsService = statisticsService;
        _logger = logger;
        (_from, _to) = GetWeekRange(DateOnly.FromDateTime(DateTime.Now));
        SyncCustomInputs();
    }

    /// <summary>重新载入当前区间的统计数据（进入统计页或窗口重新显示时调用）。</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var report = await _statisticsService.GetAsync(_from, _to, today);

            Project(report, today);
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载统计数据失败");
            ErrorText = WindowText.STATS_LOAD_FAILED;
        }
    }

    /// <summary>应用自定义区间：校验格式与先后顺序后才查询。</summary>
    [RelayCommand]
    private async Task ApplyCustomRangeAsync()
    {
        if (!TryParseDate(CustomFromText, out var from) || !TryParseDate(CustomToText, out var to))
        {
            ErrorText = WindowText.STATS_INVALID_DATE;
            return;
        }

        if (to < from)
        {
            ErrorText = WindowText.STATS_INVALID_RANGE;
            return;
        }

        _from = from;
        _to = to;
        ErrorText = string.Empty;
        await RefreshAsync();
    }

    /// <summary>切换区间：本周/本月立刻重查；自定义先把当前区间回填到输入框等用户点「应用」。</summary>
    /// <param name="range">目标区间。</param>
    private void SelectRange(StatsRange range)
    {
        if (_range == range)
        {
            return;
        }

        _range = range;
        OnPropertyChanged(nameof(IsThisWeek));
        OnPropertyChanged(nameof(IsThisMonth));
        OnPropertyChanged(nameof(IsCustom));

        if (range == StatsRange.Custom)
        {
            SyncCustomInputs();
            ErrorText = string.Empty;
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        (_from, _to) = range == StatsRange.ThisWeek ? GetWeekRange(today) : GetMonthRange(today);
        ErrorText = string.Empty;
        _ = RefreshAsync();
    }

    /// <summary>把聚合结果投影成界面可绑定的文案与行集合。</summary>
    /// <param name="report">聚合结果。</param>
    /// <param name="today">今天（用于高亮最近 7 天里的当天）。</param>
    private void Project(StatisticsReport report, DateOnly today)
    {
        HasData = report.HasData;
        RangeText = string.Format(CultureInfo.CurrentCulture, WindowText.STATS_RANGE_FORMAT, _from, _to);

        CompletionRateText = string.Format(CultureInfo.CurrentCulture, WindowText.STATS_RATE_FORMAT, report.CompletionRate);
        ActualHoursText = FormatHours(report.ActualMinutes);
        DoneCountText = report.DoneCount.ToString(CultureInfo.CurrentCulture);

        RateSubtitleText = string.Format(CultureInfo.CurrentCulture, WindowText.STATS_RECORD_COUNT_FORMAT, report.TotalCount);
        PlannedSubtitleText = string.Format(CultureInfo.CurrentCulture, WindowText.STATS_PLANNED_FORMAT, report.PlannedMinutes / (double)TimeDefaults.MINUTES_PER_HOUR);
        DoneSubtitleText = string.Format(CultureInfo.CurrentCulture, WindowText.STATS_SKIP_MISSED_FORMAT, report.SkippedCount, report.MissedCount);

        SubjectRows = report.Subjects.Select(CreateSubjectRow).ToList();
        DayRows = report.RecentDays.Select(day => CreateDayRow(day, today)).ToList();
    }

    /// <summary>科目行：横条占比、科目色与明细文案。</summary>
    /// <param name="stat">科目统计切片。</param>
    private static SubjectStatRow CreateSubjectRow(SubjectStatistics stat)
    {
        var detail = string.Format(
            CultureInfo.CurrentCulture,
            WindowText.STATS_SUBJECT_DETAIL_FORMAT,
            stat.SharePercent,
            stat.PlannedMinutes);

        return new SubjectStatRow(
            stat.SubjectName,
            ToBrush(stat.SubjectColor),
            detail,
            Math.Clamp(stat.SharePercent, 0, UiConstants.STATS_PERCENT_FULL));
    }

    /// <summary>最近 7 天的一行：无记录时显示占位而不是 0%。</summary>
    /// <param name="day">单日统计。</param>
    /// <param name="today">今天。</param>
    private static DayStatRow CreateDayRow(DailyStatistics day, DateOnly today)
    {
        var percentText = day.HasData
            ? string.Format(CultureInfo.CurrentCulture, WindowText.STATS_DAY_PERCENT_FORMAT, day.CompletionRate)
            : WindowText.STATS_NO_DATA;

        return new DayStatRow(
            day.Date.ToString(WindowText.STATS_DAY_LABEL_FORMAT, CultureInfo.CurrentCulture),
            percentText,
            day.HasData ? Math.Clamp(day.CompletionRate, 0, UiConstants.STATS_PERCENT_FULL) : 0,
            day.Date == today);
    }

    /// <summary>分钟转小时文案。</summary>
    /// <param name="minutes">分钟数。</param>
    private static string FormatHours(int minutes) => string.Format(
        CultureInfo.CurrentCulture,
        WindowText.STATS_HOURS_FORMAT,
        minutes / (double)TimeDefaults.MINUTES_PER_HOUR);

    /// <summary>取 today 所在周的周一 ~ 周日。</summary>
    /// <param name="today">今天。</param>
    private static (DateOnly From, DateOnly To) GetWeekRange(DateOnly today)
    {
        var offset = ((int)today.DayOfWeek + UiConstants.DAYS_FROM_SUNDAY_TO_MONDAY) % UiConstants.DAYS_PER_WEEK;
        var from = today.AddDays(-offset);
        return (from, from.AddDays(UiConstants.DAYS_PER_WEEK - 1));
    }

    /// <summary>取 today 所在月的第一天 ~ 最后一天。</summary>
    /// <param name="today">今天。</param>
    private static (DateOnly From, DateOnly To) GetMonthRange(DateOnly today)
    {
        var from = new DateOnly(today.Year, today.Month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }

    /// <summary>把当前区间回填到自定义输入框。</summary>
    private void SyncCustomInputs()
    {
        CustomFromText = _from.ToString(WindowText.STATS_DATE_FORMAT, CultureInfo.InvariantCulture);
        CustomToText = _to.ToString(WindowText.STATS_DATE_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>严格按 yyyy-MM-dd 解析自定义日期。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="date">解析结果。</param>
    private static bool TryParseDate(string text, out DateOnly date) => DateOnly.TryParseExact(
        text.Trim(),
        WindowText.STATS_DATE_FORMAT,
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out date);

    /// <summary>十六进制颜色转 Brush；非法值退回中性灰，避免一条脏数据拖垮整页。</summary>
    /// <param name="hex">十六进制色值。</param>
    private static Brush ToBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
        catch (NotSupportedException)
        {
            return Brushes.Gray;
        }
    }

    /// <summary>无数据时显示空态。</summary>
    partial void OnHasDataChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>错误文案变化时同步刷新 <see cref="HasError"/>。</summary>
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>科目分布的一行（M4b）。</summary>
/// <param name="Name">科目名（或「未分类」）。</param>
/// <param name="Brush">科目色。</param>
/// <param name="DetailText">占比与计划时长的明细文案。</param>
/// <param name="SharePercent">横条占比（0~100）。</param>
public sealed record SubjectStatRow(string Name, Brush Brush, string DetailText, double SharePercent)
{
    /// <summary>横条剩余部分占比。</summary>
    public double RemainPercent => Math.Max(0, UiConstants.STATS_PERCENT_FULL - SharePercent);
}

/// <summary>最近 7 天柱状条的一行（M4b）。</summary>
/// <param name="LabelText">日期标签（MM-dd）。</param>
/// <param name="PercentText">完成率文案；无记录时为占位符。</param>
/// <param name="BarPercent">柱高占比（0~100）。</param>
/// <param name="IsToday">是否今天（界面高亮）。</param>
public sealed record DayStatRow(string LabelText, string PercentText, double BarPercent, bool IsToday)
{
    /// <summary>柱状条剩余高度占比。</summary>
    public double RemainPercent => Math.Max(0, UiConstants.STATS_PERCENT_FULL - BarPercent);
}
