using FluentAssertions;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;

namespace FuShi.Tests;

/// <summary>
/// 提醒规则解析的纯函数测试（M4c）：规则优先、准点、回落全局、负偏移兜底。
/// </summary>
public sealed class ReminderRuleResolverTests
{
    [Fact]
    public void ResolveLeadMinutes_ShouldPreferBeforeStartRuleOverGlobal()
    {
        var rules = new[] { Rule(TriggerType.BeforeStart, 15) };

        ReminderRuleResolver.ResolveLeadMinutes(rules, 5).Should().Be(15, "物主规则优先于全局提前量");
    }

    [Fact]
    public void ResolveLeadMinutes_ShouldTakeEarliestAmongMultipleBeforeStartRules()
    {
        var rules = new[] { Rule(TriggerType.BeforeStart, 5), Rule(TriggerType.BeforeStart, 20) };

        ReminderRuleResolver.ResolveLeadMinutes(rules, 10).Should().Be(20, "多条提前规则取最早提醒");
    }

    [Fact]
    public void ResolveLeadMinutes_ShouldBeZeroWhenOnlyAtStartRule()
    {
        var rules = new[] { Rule(TriggerType.AtStart, 0) };

        ReminderRuleResolver.ResolveLeadMinutes(rules, 10).Should().Be(0, "准点规则明确不要提前");
    }

    [Fact]
    public void ResolveLeadMinutes_ShouldFallBackToGlobalWhenNoRules()
    {
        ReminderRuleResolver.ResolveLeadMinutes(Array.Empty<ReminderRule>(), 10).Should().Be(10);
        ReminderRuleResolver.ResolveLeadMinutes(new[] { Rule(TriggerType.AtEnd, 0) }, 10).Should().Be(10);
    }

    [Fact]
    public void ResolveLeadMinutes_ShouldClampNegativeOffset()
    {
        var rules = new[] { Rule(TriggerType.BeforeStart, -30) };

        ReminderRuleResolver.ResolveLeadMinutes(rules, 10).Should().Be(0, "提前量不能把触发点推到计划时刻之后");
    }

    [Fact]
    public void ResolveMaxLeadMinutes_ShouldCoverLargerRuleOffsets()
    {
        var rules = new[] { Rule(TriggerType.BeforeStart, 60) };

        ReminderRuleResolver.ResolveMaxLeadMinutes(rules, 10).Should().Be(60);
        ReminderRuleResolver.ResolveMaxLeadMinutes(Array.Empty<ReminderRule>(), 10).Should().Be(10);
    }

    private static ReminderRule Rule(TriggerType trigger, int offset) => new()
    {
        TriggerType = trigger,
        OffsetMinutes = offset,
    };
}
