using FluentAssertions;
using FuShi.Domain.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// 链式顺延纯算法的单测：覆盖只推被点条、真重叠才连带、不再重叠即终止、两种情况触顶、累计叠加与边界。
/// </summary>
public sealed class ShiftChainTests
{
    private const int SHIFT = 10;

    [Fact]
    public void Apply_ShouldShiftOnlyTarget_WhenNextDoesNotOverlap()
    {
        var items = new[]
        {
            Item(1, At(14, 0), At(14, 45)),
            Item(2, At(15, 0), At(16, 0)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.CeilingReached.Should().BeFalse();
        result.Changes.Should().SatisfyRespectively(
            change =>
            {
                change.OccurrenceId.Should().Be(1);
                change.NewShiftMinutes.Should().Be(SHIFT);
            });
    }

    [Fact]
    public void Apply_ShouldChainShift_WhenNextIsSqueezed()
    {
        // 目标 14:00–14:45 顺延到 14:10–14:55，压到 14:30 那节课 → 它也被推。
        var items = new[]
        {
            Item(1, At(14, 0), At(14, 45)),
            Item(2, At(14, 30), At(15, 30)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.CeilingReached.Should().BeFalse();
        result.Changes.Should().SatisfyRespectively(
            change =>
            {
                change.OccurrenceId.Should().Be(1);
                change.NewShiftMinutes.Should().Be(SHIFT);
            },
            change =>
            {
                change.OccurrenceId.Should().Be(2);
                change.NewShiftMinutes.Should().Be(SHIFT);
            });
    }

    [Fact]
    public void Apply_ShouldStopChain_WhenSecondHopDoesNotOverlap()
    {
        // 目标推进后压到 14:30；但 14:30 被推后（到 15:00 结束）仍未压到 15:30 → 链条到 14:30 即止。
        var items = new[]
        {
            Item(1, At(14, 0), At(14, 25)),
            Item(2, At(14, 30), At(14, 50)),
            Item(3, At(15, 30), At(16, 0)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.Changes.Should().HaveCount(2, "14:25 顺延到 14:35 压到了第二条；第三条不受影响");
        result.Changes.Select(static change => change.OccurrenceId).Should().Equal(1, 2);
    }

    [Fact]
    public void Apply_ShouldAccumulateExistingShift()
    {
        var items = new[] { Item(1, At(14, 0), At(15, 0), shiftMinutes: SHIFT) };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.Changes.Should().ContainSingle()
            .Which.NewShiftMinutes.Should().Be(SHIFT * 2, "旧的顺延量要在此基础上继续叠加");
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenTargetNotInBatch()
    {
        var items = new[] { Item(1, At(14, 0), At(14, 45)) };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 99, shiftMinutes: SHIFT);

        result.Should().BeSameAs(ShiftChainResult.Empty);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenShiftIsNotPositive()
    {
        var items = new[] { Item(1, At(14, 0), At(14, 45)) };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: 0);

        result.Should().BeSameAs(ShiftChainResult.Empty);
    }

    [Fact]
    public void Apply_ShouldReachCeiling_WhenCrossingMidnight()
    {
        // 跨午夜时段：23:30 起、次日 00:30 毕。顺延 30 分钟会使落点 = 次日 00:00 → 触顶。
        var items = new[]
        {
            Item(1, At(23, 30), At(23, 30).AddDays(1).AddHours(1)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: 30);

        result.CeilingReached.Should().BeTrue();
        result.Changes.Should().BeEmpty("落点越过当天 23:59，该条不再顺延");
    }

    [Fact]
    public void Apply_ShouldReachCeiling_WhenPastOwnCourseEnd()
    {
        // 短课 14:00–14:05，顺延 10 分钟落点 14:10 > 课程结束 14:05 → 触顶（不给已下课的时段发陈旧提醒）。
        var items = new[]
        {
            Item(1, At(14, 0), At(14, 5)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.CeilingReached.Should().BeTrue();
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ShouldKeepEarlierShifts_AndStopAtCeiling()
    {
        // 第一条推得动、第二条触顶：应保留第一条的变更并标记触顶，链条在第二条终止。
        var items = new[]
        {
            Item(1, At(14, 0), At(14, 45)),
            Item(2, At(14, 30), At(14, 35)),
            Item(3, At(15, 30), At(16, 0)),
        };

        var result = ShiftChain.Apply(items, targetOccurrenceId: 1, shiftMinutes: SHIFT);

        result.CeilingReached.Should().BeTrue();
        result.Changes.Should().ContainSingle().Which.OccurrenceId.Should().Be(1);
    }

    /// <summary>构造一条当天的顺延输入项。</summary>
    private static ShiftChainItem Item(int id, DateTime start, DateTime end, int shiftMinutes = 0) =>
        new(id, start, end, shiftMinutes);

    /// <summary>以固定日期拼装当天某时刻。</summary>
    private static DateTime At(int hour, int minute) =>
        new DateTime(2026, 10, 19, hour, minute, 0, DateTimeKind.Unspecified);
}
