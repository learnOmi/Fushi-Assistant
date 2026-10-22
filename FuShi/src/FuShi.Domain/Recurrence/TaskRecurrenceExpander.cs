using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Time;

namespace FuShi.Domain.Recurrence;

/// <summary>
/// 事项展开（M4c）：把 <see cref="TaskItem"/> 的「截止时刻 + 重复档位」在指定区间内展开成计划实例。
/// 纯算法——不碰数据库、不读系统时钟，区间由调用方给定，可 100% 单测。
/// <para>
/// <b>受控枚举文本</b>：<see cref="TaskItem.RepeatRule"/> 只认 <c>NONE</c> / <c>DAILY</c> / <c>WEEKLY</c>
/// （大小写不敏感），其余值按 <c>NONE</c> 处理。M4 不解析完整 RRULE：
/// 字段仍是字符串，将来换成真正的解析器时不需要改表。
/// </para>
/// <para>
/// 事项<b>没有时长</b>：<c>EndsAt</c> 与 <c>PlannedAt</c> 相同，提醒是一次性的到点通知，
/// 不参与「进行中到点自动完成」。时区归一化与课表同一口径，跨 DST 的无效/歧义时刻显式处理。
/// </para>
/// </summary>
public static class TaskRecurrenceExpander
{
    /// <summary>一次性事项。</summary>
    public const string RULE_NONE = "NONE";

    /// <summary>每天同一时刻重复。</summary>
    public const string RULE_DAILY = "DAILY";

    /// <summary>每周同一星期几重复。</summary>
    public const string RULE_WEEKLY = "WEEKLY";

    /// <summary>事项没有科目；占位值只用于满足账本模型，不参与上屏取色。</summary>
    private const int NO_SUBJECT_ID = 0;

    /// <summary>每周的天数（WEEKLY 的步进）。</summary>
    private const int DAYS_PER_WEEK = 7;

    /// <summary>
    /// 展开单个事项。
    /// <list type="bullet">
    /// <item>状态不是 <see cref="TaskItemStatus.Pending"/>（已完成/已跳过）时直接不展开。</item>
    /// <item><see cref="TaskItem.DueAt"/> 为空：只显示不提醒，故返回空。</item>
    /// <item><c>NONE</c>：截止日落在区间内才出一条；<c>DAILY</c>：从截止日起逐日；<c>WEEKLY</c>：每 7 天一条。</item>
    /// </list>
    /// </summary>
    /// <param name="task">事项。</param>
    /// <param name="from">目标区间起（含）。</param>
    /// <param name="to">目标区间止（含）。</param>
    /// <param name="timeZone">目标时区。</param>
    public static ExpansionResult Expand(TaskItem task, DateOnly from, DateOnly to, TimeZoneInfo timeZone)
    {
        var occurrences = new List<PlannedOccurrence>();
        var adjustments = new List<DstAdjustment>();

        if (task.Status != TaskItemStatus.Pending || task.DueAt is not { } due)
        {
            return new ExpansionResult(occurrences, adjustments);
        }

        var rule = NormalizeRule(task.RepeatRule);
        var dueDate = DateOnly.FromDateTime(due);
        var dueTime = TimeOnly.FromDateTime(due);

        switch (rule)
        {
            case RULE_DAILY:
                AddRepeating(task, dueDate, dueTime, 1, from, to, timeZone, occurrences, adjustments);
                break;

            case RULE_WEEKLY:
                AddRepeating(task, dueDate, dueTime, DAYS_PER_WEEK, from, to, timeZone, occurrences, adjustments);
                break;

            default:
                if (dueDate >= from && dueDate <= to)
                {
                    AddOccurrence(task, dueDate, dueTime, timeZone, occurrences, adjustments);
                }

                break;
        }

        return new ExpansionResult(occurrences, adjustments);
    }

    /// <summary>批量展开多个事项；实例按计划时刻升序。</summary>
    /// <param name="tasks">事项集合。</param>
    /// <param name="from">目标区间起（含）。</param>
    /// <param name="to">目标区间止（含）。</param>
    /// <param name="timeZone">目标时区。</param>
    public static ExpansionResult Expand(
        IEnumerable<TaskItem> tasks,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone)
    {
        var all = new List<PlannedOccurrence>();
        var adjustments = new List<DstAdjustment>();

        foreach (var task in tasks)
        {
            var result = Expand(task, from, to, timeZone);
            all.AddRange(result.Occurrences);
            adjustments.AddRange(result.Adjustments);
        }

        all.Sort(static (a, b) => a.PlannedAt.CompareTo(b.PlannedAt));
        return new ExpansionResult(all, adjustments);
    }

    /// <summary>把受控文本归一化为三档之一；未知或空白按一次性处理。</summary>
    /// <param name="rule">原始规则文本。</param>
    public static string NormalizeRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return RULE_NONE;
        }

        return rule.Trim().ToUpperInvariant() switch
        {
            RULE_DAILY => RULE_DAILY,
            RULE_WEEKLY => RULE_WEEKLY,
            _ => RULE_NONE,
        };
    }

    /// <summary>按固定步长（1 天 / 7 天）从截止日展开，只保留落进区间的日期。</summary>
    private static void AddRepeating(
        TaskItem task,
        DateOnly dueDate,
        TimeOnly dueTime,
        int stepDays,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone,
        List<PlannedOccurrence> sink,
        List<DstAdjustment> adjustments)
    {
        for (var day = dueDate; day <= to; day = day.AddDays(stepDays))
        {
            if (day < from)
            {
                continue;
            }

            AddOccurrence(task, day, dueTime, timeZone, sink, adjustments);
        }
    }

    /// <summary>展开某一天的一条实例并做时区归一化，改动写进调整明细（绝不静默丢弃）。</summary>
    private static void AddOccurrence(
        TaskItem task,
        DateOnly day,
        TimeOnly dueTime,
        TimeZoneInfo timeZone,
        List<PlannedOccurrence> sink,
        List<DstAdjustment> adjustments)
    {
        var original = day.ToDateTime(dueTime);
        var resolution = TimeZoneResolver.Normalize(original, timeZone);

        if (resolution.Kind != DstAdjustmentKind.None)
        {
            adjustments.Add(new DstAdjustment(
                OwnerType.Task,
                task.Id,
                original,
                resolution.LocalTime,
                resolution.Kind));
        }

        sink.Add(new PlannedOccurrence(
            OwnerType.Task,
            task.Id,
            resolution.LocalTime,
            resolution.LocalTime,
            NO_SUBJECT_ID,
            null));
    }
}
