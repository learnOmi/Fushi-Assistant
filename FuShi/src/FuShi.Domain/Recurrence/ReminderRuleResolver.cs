using FuShi.Domain.Entities;
using FuShi.Domain.Enums;

namespace FuShi.Domain.Recurrence;

/// <summary>
/// 提醒规则解析（M4c）：按某个物主的规则算出「提前量」，无规则时回落到全局设置。
/// 纯函数——规则与全局值都由调用方传入，便于单测。
/// <para>
/// 口径：
/// <list type="bullet">
/// <item>有多条 <see cref="TriggerType.BeforeStart"/> 时取<b>最大</b>偏移（最早提醒，宁可早不可晚）。</item>
/// <item>只有 <see cref="TriggerType.AtStart"/> 时提前量为 0（准点）。</item>
/// <item>没有任何可用规则时用全局 <c>LeadTimeMinutes</c>。</item>
/// <item>负偏移一律按 0 兜底，提前量不会把触发点推到计划时刻之后。</item>
/// </list>
/// </para>
/// </summary>
public static class ReminderRuleResolver
{
    /// <summary>解析某物主的提前提醒分钟数。</summary>
    /// <param name="rules">该物主的全部提醒规则（可为空）。</param>
    /// <param name="globalLeadMinutes">全局提前量。</param>
    public static int ResolveLeadMinutes(IEnumerable<ReminderRule> rules, int globalLeadMinutes)
    {
        var beforeStart = rules
            .Where(static rule => rule.TriggerType == TriggerType.BeforeStart)
            .Select(static rule => Math.Max(0, rule.OffsetMinutes))
            .DefaultIfEmpty(-1)
            .Max();

        if (beforeStart >= 0)
        {
            return beforeStart;
        }

        if (rules.Any(static rule => rule.TriggerType == TriggerType.AtStart))
        {
            return 0;
        }

        return Math.Max(0, globalLeadMinutes);
    }

    /// <summary>
    /// 解析一批规则里可能出现的最大提前量。调度服务用它界定 SQL 候选窗口，
    /// 避免「规则提前量大于全局值」的实例被窗口过滤掉。
    /// </summary>
    /// <param name="rules">全部规则。</param>
    /// <param name="globalLeadMinutes">全局提前量。</param>
    public static int ResolveMaxLeadMinutes(IEnumerable<ReminderRule> rules, int globalLeadMinutes)
    {
        var max = Math.Max(0, globalLeadMinutes);
        foreach (var rule in rules)
        {
            if (rule.TriggerType == TriggerType.BeforeStart && rule.OffsetMinutes > max)
            {
                max = rule.OffsetMinutes;
            }
        }

        return max;
    }
}
