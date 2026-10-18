using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Domain.Time;

namespace FuShi.Tests;

/// <summary>
/// RecurrenceExpander 的单元测试：周期匹配、阶段区间交集、跨午夜、DST 归一化与批内排序。
/// now 无关紧要（纯展开），因此断言全部是「确定的结构」。
/// </summary>
public class RecurrenceExpanderTests
{
    private static readonly TimeZoneInfo London = TimeZoneResolver.Resolve(TimeDefaults.DEFAULT_TIMEZONE_ID).Zone;

    private static Phase MakePhase(DateOnly start, DateOnly? end = null) => new()
    {
        Id = 1,
        Name = "测试阶段",
        StartDate = start,
        EndDate = end,
        IsActive = true,
    };

    private static ScheduleEntry MakeEntry(
        DayOfWeek dayOfWeek,
        TimeOnly start,
        TimeOnly end,
        int id = 7,
        bool enabled = true) => new()
    {
        Id = id,
        PhaseId = 1,
        DayOfWeek = dayOfWeek,
        StartTime = start,
        EndTime = end,
        SubjectId = 3,
        Enabled = enabled,
    };

    [Fact]
    public void Expand_ShouldMatchWeeklyTemplateAcrossRange()
    {
        // 2026 年 3 月的周日为 1 / 8 / 15 / 22 / 29，共 5 天。
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var entry = MakeEntry(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(11, 0));

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            London);

        result.Occurrences.Should().HaveCount(5);
        result.Occurrences.Should().OnlyContain(o => o.PlannedAt.DayOfWeek == DayOfWeek.Sunday);
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 3, 1, 10, 0, 0));
        result.Occurrences[0].EndsAt.Should().Be(new DateTime(2026, 3, 1, 11, 0, 0));
        result.Adjustments.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ShouldShiftInvalidDstTimeForward()
    {
        // 2026-03-29 是英国夏令时开始日：周日 01:30 属于「被跳过的一小时」。
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var entry = MakeEntry(DayOfWeek.Sunday, new TimeOnly(1, 30), new TimeOnly(2, 30));

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 3, 29),
            new DateOnly(2026, 3, 29),
            London);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 3, 29, 2, 0, 0));
        result.Occurrences[0].EndsAt.Should().Be(new DateTime(2026, 3, 29, 2, 30, 0));

        // 调整必须被记录（绝不静默丢弃）。
        result.Adjustments.Should().ContainSingle();
        result.Adjustments[0].Kind.Should().Be(DstAdjustmentKind.InvalidShiftedForward);
        result.Adjustments[0].Original.Should().Be(new DateTime(2026, 3, 29, 1, 30, 0));
        result.Adjustments[0].Adjusted.Should().Be(new DateTime(2026, 3, 29, 2, 0, 0));
    }

    [Fact]
    public void Expand_ShouldTreatCrossMidnightAsSingleContinuousSpan()
    {
        // 周六 23:30 → 次日 00:30：视为一个连续时段，结束落在次日，不拆两天。
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var entry = MakeEntry(DayOfWeek.Saturday, new TimeOnly(23, 30), new TimeOnly(0, 30));

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 5, 2),
            new DateOnly(2026, 5, 2),
            London);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 5, 2, 23, 30, 0));
        result.Occurrences[0].EndsAt.Should().Be(new DateTime(2026, 5, 3, 0, 30, 0));
    }

    [Fact]
    public void Expand_ShouldSkipDisabledEntry()
    {
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var entry = MakeEntry(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), enabled: false);

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            London);

        result.Occurrences.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ShouldIntersectPhaseDateRange()
    {
        // 阶段仅 03-10..03-20 生效，此间唯一的周日是 03-15。
        var phase = MakePhase(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20));
        var entry = MakeEntry(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(11, 0));

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            London);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 3, 15, 10, 0, 0));
    }

    [Fact]
    public void Expand_Batch_ShouldReturnChronologicallySortedResults()
    {
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var laterEntry = MakeEntry(DayOfWeek.Sunday, new TimeOnly(20, 0), new TimeOnly(21, 0), id: 2);
        var earlierEntry = MakeEntry(DayOfWeek.Sunday, new TimeOnly(8, 0), new TimeOnly(9, 0), id: 1);

        var result = RecurrenceExpander.Expand(
            new[] { laterEntry, earlierEntry },
            phase,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 1),
            London);

        result.Occurrences.Should().HaveCount(2);
        result.Occurrences[0].PlannedAt.Should().BeBefore(result.Occurrences[1].PlannedAt);
        result.Occurrences[0].SourceId.Should().Be(1);
    }

    [Fact]
    public void Expand_ShouldCarrySourceAndSubjectIdentifiers()
    {
        var phase = MakePhase(new DateOnly(2026, 1, 1));
        var entry = MakeEntry(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), id: 42);

        var result = RecurrenceExpander.Expand(
            entry,
            phase,
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 2),
            London);

        var occurrence = result.Occurrences.Should().ContainSingle().Subject;
        occurrence.SourceType.Should().Be(OwnerType.ScheduleEntry);
        occurrence.SourceId.Should().Be(42);
        occurrence.SubjectId.Should().Be(3);
    }
}
