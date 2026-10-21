using FluentAssertions;
using FuShi.Domain.Assistant;
using FuShi.Domain.Enums;
using FuShi.Domain.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// 表情信号提炼的单测：覆盖在课判定、临期向上取整、刚下课向下取整、后续课排除跳过、
/// 日程冲突、整日完成门槛与闲置时长夹取。
/// </summary>
public sealed class BotSignalBuilderTests
{
    private static readonly DateTime NOW = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Build_ShouldReportInClass_WhenNowInsideSlot()
    {
        var slots = new[] { Slot(9, 0, 11, 0, OccurrenceStatus.InProgress) };

        var signals = BotSignalBuilder.Build(slots, NOW, NOW);

        signals.InClass.Should().BeTrue();
        signals.CurrentStatus.Should().Be(OccurrenceStatus.InProgress);
        signals.MinutesUntilNext.Should().BeNull("上课中不关心下一节");
    }

    [Fact]
    public void Build_ShouldCeilMinutesUntilNext_AndSkipSkipped()
    {
        var slots = new[]
        {
            Slot(10, 4, 30, 10, 50, OccurrenceStatus.Pending),
            Slot(10, 5, 0, 11, 0, OccurrenceStatus.Skipped),
        };

        var signals = BotSignalBuilder.Build(slots, NOW, NOW);

        signals.InClass.Should().BeFalse();
        signals.MinutesUntilNext.Should().Be(5, "4 分 30 秒按 5 分钟计；被跳过的 10:05 不参与");
    }

    [Fact]
    public void Build_ShouldFloorMinutesSinceLastEnd()
    {
        // 现在 10:00，上一节 09:50 下课 → 9 分 59 秒按 9 分钟计。
        var slots = new[] { Slot(9, 30, 9, 50, OccurrenceStatus.Done) };
        var now = new DateTime(2026, 10, 21, 10, 0, 0).AddSeconds(-1);

        var signals = BotSignalBuilder.Build(slots, now, now);

        signals.MinutesSinceLastEnd.Should().Be(9);
        signals.HasFollowingToday.Should().BeFalse();
    }

    [Fact]
    public void Build_ShouldExcludeSkippedFromFollowingToday()
    {
        var slots = new[]
        {
            Slot(9, 0, 9, 45, OccurrenceStatus.Done),
            Slot(11, 0, 12, 0, OccurrenceStatus.Skipped),
        };

        var signals = BotSignalBuilder.Build(slots, NOW, NOW);

        signals.MinutesSinceLastEnd.Should().Be(15);
        signals.HasFollowingToday.Should().BeFalse("今天只剩一节被跳过的课");
    }

    [Fact]
    public void Build_ShouldDetectConflict_WhenSlotsOverlap()
    {
        var slots = new[]
        {
            Slot(9, 0, 10, 30, OccurrenceStatus.Pending),
            Slot(10, 0, 11, 0, OccurrenceStatus.Pending),
        };

        BotSignalBuilder.Build(slots, NOW, NOW).HasConflict.Should().BeTrue();
    }

    [Fact]
    public void Build_ShouldNotDetectConflict_WhenSlotsAreAdjacent()
    {
        var slots = new[]
        {
            Slot(8, 0, 9, 0, OccurrenceStatus.Done),
            Slot(9, 0, 10, 0, OccurrenceStatus.Done),
        };

        BotSignalBuilder.Build(slots, NOW, NOW).HasConflict.Should().BeFalse();
    }

    [Fact]
    public void Build_ShouldIgnoreConflict_WhenItAlreadyEnded()
    {
        var slots = new[]
        {
            Slot(8, 0, 9, 30, OccurrenceStatus.Done),
            Slot(8, 30, 9, 0, OccurrenceStatus.Done),
        };

        BotSignalBuilder.Build(slots, NOW, NOW).HasConflict.Should().BeFalse("早上的冲突已经过去，晚上启动不该再惊讶一次");
    }

    [Fact]
    public void Build_ShouldIgnoreSkippedSlot_WhenCheckingConflict()
    {
        var slots = new[]
        {
            Slot(9, 0, 10, 30, OccurrenceStatus.Pending),
            Slot(10, 0, 11, 0, OccurrenceStatus.Skipped),
        };

        BotSignalBuilder.Build(slots, NOW, NOW).HasConflict.Should().BeFalse("被跳过的课不算冲突");
    }

    [Fact]
    public void Build_ShouldMarkAllDone_WhenThreeSlotsDone()
    {
        var slots = new[]
        {
            Slot(8, 0, 9, 0, OccurrenceStatus.Done),
            Slot(9, 0, 10, 0, OccurrenceStatus.Done),
            Slot(11, 0, 12, 0, OccurrenceStatus.Done),
        };
        var noon = new DateTime(2026, 10, 21, 12, 30, 0);

        BotSignalBuilder.Build(slots, noon, noon).AllDoneToday.Should().BeTrue();
    }

    [Fact]
    public void Build_ShouldNotMarkAllDone_WhenFewerThanThreeOrOneSkipped()
    {
        var twoDone = new[]
        {
            Slot(8, 0, 9, 0, OccurrenceStatus.Done),
            Slot(9, 0, 10, 0, OccurrenceStatus.Done),
        };
        var oneSkipped = new[]
        {
            Slot(8, 0, 9, 0, OccurrenceStatus.Done),
            Slot(9, 0, 10, 0, OccurrenceStatus.Done),
            Slot(11, 0, 12, 0, OccurrenceStatus.Skipped),
        };
        var noon = new DateTime(2026, 10, 21, 12, 30, 0);

        BotSignalBuilder.Build(twoDone, noon, noon).AllDoneToday.Should().BeFalse("少于三节不庆祝");
        BotSignalBuilder.Build(oneSkipped, noon, noon).AllDoneToday.Should().BeFalse("跳过不等于完成");
    }

    [Fact]
    public void Build_ShouldClampIdleFor_WhenInteractionIsInFuture()
    {
        var future = NOW.AddMinutes(5);

        var signals = BotSignalBuilder.Build(Array.Empty<TimelineSlot>(), NOW, future);

        signals.IdleFor.Should().Be(TimeSpan.Zero);
        signals.InClass.Should().BeFalse();
        signals.MinutesUntilNext.Should().BeNull();
        signals.MinutesSinceLastEnd.Should().BeNull();
        signals.AllDoneToday.Should().BeFalse();
    }

    [Fact]
    public void Build_ShouldPassThroughSevenDayFlag()
    {
        BotSignalBuilder.Build(Array.Empty<TimelineSlot>(), NOW, NOW, allDoneForSevenDays: true)
            .AllDoneForSevenDays.Should().BeTrue();
    }

    /// <summary>构造当天某时段。</summary>
    private static TimelineSlot Slot(
        int startHour, int startMinute, int endHour, int endMinute,
        OccurrenceStatus? status)
    {
        var startsAt = new DateTime(2026, 10, 21, startHour, startMinute, 0);
        var endsAt = new DateTime(2026, 10, 21, endHour, endMinute, 0);
        return new TimelineSlot(startsAt, endsAt, 1, "数学", "#FFFFFF", null, status);
    }

    /// <summary>构造带秒的时段（用于取整边界）。</summary>
    private static TimelineSlot Slot(
        int startHour, int startMinute, int startSecond,
        int endHour, int endMinute,
        OccurrenceStatus? status)
    {
        var startsAt = new DateTime(2026, 10, 21, startHour, startMinute, startSecond);
        var endsAt = new DateTime(2026, 10, 21, endHour, endMinute, 0);
        return new TimelineSlot(startsAt, endsAt, 1, "数学", "#FFFFFF", null, status);
    }
}
