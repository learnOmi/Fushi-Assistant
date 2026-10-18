using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Time;

namespace FuShi.Domain.Recurrence;

/// <summary>
/// 周期展开：把「周几 + 起止时刻」的课表模板，在指定日期区间内展开成具体的计划实例。
/// 纯算法——不碰数据库、不读系统时钟；区间由调用方给定，因此可 100% 单测。
/// </summary>
public static class RecurrenceExpander
{
    /// <summary>
    /// 展开单条课表条目。
    /// 规则：
    /// <list type="bullet">
    /// <item>模板按 <see cref="ScheduleEntry.DayOfWeek"/> 逐日匹配，并与阶段生效区间取交集。</item>
    /// <item><c>EndTime &lt; StartTime</c> 视为「跨午夜的一个连续时段」，结束时刻落在次日，不拆两天。</item>
    /// <item>起止时刻均过 <see cref="TimeZoneResolver"/> 归一化，处理英国 DST 的无效/歧义时刻。</item>
    /// <item><see cref="ScheduleEntry.Enabled"/> 为 false 时直接跳过。</item>
    /// </list>
    /// </summary>
    /// <param name="entry">课表模板。</param>
    /// <param name="phase">模板所属阶段，用于收窄生效日期区间。</param>
    /// <param name="from">目标区间起（含）。</param>
    /// <param name="to">目标区间止（含）。</param>
    /// <param name="timeZone">目标时区。</param>
    public static ExpansionResult Expand(
        ScheduleEntry entry,
        Phase phase,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone)
    {
        var occurrences = new List<PlannedOccurrence>();
        var adjustments = new List<DstAdjustment>();

        if (!entry.Enabled)
        {
            return new ExpansionResult(occurrences, adjustments);
        }

        // 与阶段生效区间取交集：阶段没生效的日子不展开。
        var windowStart = from > phase.StartDate ? from : phase.StartDate;
        var windowEnd = to;
        if (phase.EndDate.HasValue && phase.EndDate.Value < windowEnd)
        {
            windowEnd = phase.EndDate.Value;
        }

        for (var day = windowStart; day <= windowEnd; day = day.AddDays(1))
        {
            if (day.DayOfWeek != entry.DayOfWeek)
            {
                continue;
            }

            var startLocal = day.ToDateTime(entry.StartTime);
            var endLocal = day.ToDateTime(entry.EndTime);

            // 跨午夜：结束时刻早于开始时刻 → 顺延到次日，视为同一个连续时段。
            if (entry.EndTime < entry.StartTime)
            {
                endLocal = endLocal.AddDays(1);
            }

            var startResolution = TimeZoneResolver.Normalize(startLocal, timeZone);
            var endResolution = TimeZoneResolver.Normalize(endLocal, timeZone);

            CollectAdjustment(adjustments, entry, startLocal, startResolution);
            CollectAdjustment(adjustments, entry, endLocal, endResolution);

            occurrences.Add(new PlannedOccurrence(
                OwnerType.ScheduleEntry,
                entry.Id,
                startResolution.LocalTime,
                endResolution.LocalTime,
                entry.SubjectId,
                entry.Note));
        }

        return new ExpansionResult(occurrences, adjustments);
    }

    /// <summary>批量展开多条课表条目，合并结果；实例按计划开始时刻升序。</summary>
    /// <param name="entries">课表模板集合。</param>
    /// <param name="phase">所属阶段（本批模板共享同一阶段）。</param>
    /// <param name="from">目标区间起（含）。</param>
    /// <param name="to">目标区间止（含）。</param>
    /// <param name="timeZone">目标时区。</param>
    public static ExpansionResult Expand(
        IEnumerable<ScheduleEntry> entries,
        Phase phase,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone)
    {
        var all = new List<PlannedOccurrence>();
        var adjustments = new List<DstAdjustment>();

        foreach (var entry in entries)
        {
            var result = Expand(entry, phase, from, to, timeZone);
            all.AddRange(result.Occurrences);
            adjustments.AddRange(result.Adjustments);
        }

        all.Sort(static (a, b) => a.PlannedAt.CompareTo(b.PlannedAt));
        return new ExpansionResult(all, adjustments);
    }

    /// <summary>把一次归一化中「确实发生了改动」的情况收进明细（None 不入账）。</summary>
    private static void CollectAdjustment(
        List<DstAdjustment> sink,
        ScheduleEntry entry,
        DateTime original,
        TimeZoneResolution resolution)
    {
        if (resolution.Kind == DstAdjustmentKind.None)
        {
            return;
        }

        sink.Add(new DstAdjustment(
            OwnerType.ScheduleEntry,
            entry.Id,
            original,
            resolution.LocalTime,
            resolution.Kind));
    }
}
