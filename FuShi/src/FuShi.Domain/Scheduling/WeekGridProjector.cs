namespace FuShi.Domain.Scheduling;

/// <summary>
/// 周网格投影：把「一周内的课表模板」揉成固定的「7 列 × N 行」网格数据。
/// 纯算法——不碰数据库、不读系统时钟（<paramref name="now"/> 由调用方传入），因此可 100% 单测。
/// <para>
/// 口径（见 M3 实施计划取舍 ①）：
/// <list type="bullet">
/// <item>行 = 出现过的<b>去重起始时刻</b>升序；新增"没出现过的时刻"走「+ 添加时段」，不靠点空格子。</item>
/// <item>列 = 周一…周日（周一打头）。</item>
/// <item>跨午夜条目（结束&lt;开始）只在<b>起始日</b>那一格出现，附「跨午夜」角标，不拆两天。</item>
/// <item>"当前高亮"取「今天这一列 + 时刻落在区间内」，与悬浮组件同口径；跨午夜条目顺带覆盖次日凌晨。</item>
/// </list>
/// </para>
/// </summary>
public static class WeekGridProjector
{
    /// <summary>一周的天数（网格列数）。</summary>
    private const int DAYS_PER_WEEK = 7;

    /// <summary>
    /// 投影出周网格。
    /// </summary>
    /// <param name="sources">课表模板集合（含停用项；停用项仍入网格，只是弱化显示）。</param>
    /// <param name="now">当前时刻（用于"当前高亮"判定）。</param>
    /// <returns>网格；无模板时返回 <see cref="WeekGrid.Empty"/>。</returns>
    public static WeekGrid Project(IEnumerable<WeekGridSource> sources, DateTime now)
    {
        if (sources is null)
        {
            return WeekGrid.Empty;
        }

        var list = sources.ToList();
        if (list.Count == 0)
        {
            return WeekGrid.Empty;
        }

        var rowTimes = list
            .Select(static source => source.StartTime)
            .Distinct()
            .OrderBy(static time => time)
            .ToList();

        var rows = new List<WeekGridRow>(rowTimes.Count);
        foreach (var time in rowTimes)
        {
            var cells = new List<WeekGridCell>(DAYS_PER_WEEK);
            for (var index = 0; index < DAYS_PER_WEEK; index++)
            {
                // 显示列 0..6 → 周一(1)..周日(0)：用 (index + 1) % 7 映射到 DayOfWeek。
                var day = DisplayIndexToDayOfWeek(index);
                var items = list
                    .Where(source => source.DayOfWeek == day && source.StartTime == time)
                    .Select(source => ToItem(source, now))
                    .ToList();

                cells.Add(new WeekGridCell(day, time, items));
            }

            rows.Add(new WeekGridRow(time, cells));
        }

        return new WeekGrid(rows);
    }

    /// <summary>把模板项投影成网格条目（计算跨午夜与"当前"标记）。</summary>
    private static WeekGridItem ToItem(WeekGridSource source, DateTime now) => new(
        source.EntryId,
        source.StartTime,
        source.EndTime,
        source.SubjectName,
        source.SubjectColor,
        source.Note,
        IsCrossMidnight(source),
        IsCurrent(source, now),
        source.Enabled);

    /// <summary>是否跨午夜：结束时刻早于开始时刻。</summary>
    private static bool IsCrossMidnight(WeekGridSource source) => source.EndTime < source.StartTime;

    /// <summary>
    /// 该模板此刻是否"正在进行"。
    /// 非跨午夜：起始日 == 今天 且 now 落在 [Start, End)。
    /// 跨午夜：起始日今天且 now >= Start（深夜段），或次日的今天且 now &lt; End（凌晨段）。
    /// </summary>
    private static bool IsCurrent(WeekGridSource source, DateTime now)
    {
        var nowTime = TimeOnly.FromDateTime(now);

        if (!IsCrossMidnight(source))
        {
            return source.DayOfWeek == now.DayOfWeek && nowTime >= source.StartTime && nowTime < source.EndTime;
        }

        var startDayMatches = source.DayOfWeek == now.DayOfWeek && nowTime >= source.StartTime;
        var nextDay = (DayOfWeek)(((int)source.DayOfWeek + 1) % DAYS_PER_WEEK);
        var nextDayMatches = nextDay == now.DayOfWeek && nowTime < source.EndTime;
        return startDayMatches || nextDayMatches;
    }

    /// <summary>显示列索引（0 = 周一 … 6 = 周日）映射到 <see cref="DayOfWeek"/>。</summary>
    private static DayOfWeek DisplayIndexToDayOfWeek(int index) => (DayOfWeek)((index + 1) % DAYS_PER_WEEK);
}
