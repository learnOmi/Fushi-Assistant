using FluentAssertions;
using FuShi.Domain.Dedup;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;

namespace FuShi.Tests;

/// <summary>
/// OccurrenceDeduplicator 的单元测试：与库中已有实例去重、批内自身去重、以及不误伤相异项。
/// </summary>
public class OccurrenceDeduplicatorTests
{
    private static PlannedOccurrence Candidate(int sourceId, DateTime plannedAt) =>
        new(OwnerType.ScheduleEntry, sourceId, plannedAt, plannedAt.AddHours(1), 1, null);

    private static Occurrence Existing(int sourceId, DateTime plannedAt) => new()
    {
        SourceType = OwnerType.ScheduleEntry,
        SourceId = sourceId,
        PlannedAt = plannedAt,
        Status = OccurrenceStatus.Fired,
    };

    [Fact]
    public void RemoveExisting_ShouldDropCandidatesAlreadyInDatabase()
    {
        var planned = new DateTime(2026, 3, 1, 10, 0, 0);
        var candidates = new[] { Candidate(7, planned), Candidate(7, planned.AddDays(7)) };
        var existing = new[] { Existing(7, planned) };

        var result = OccurrenceDeduplicator.RemoveExisting(candidates, existing);

        result.Should().ContainSingle();
        result[0].PlannedAt.Should().Be(planned.AddDays(7));
    }

    [Fact]
    public void RemoveExisting_ShouldAlsoDropDuplicatesWithinCandidates()
    {
        var planned = new DateTime(2026, 3, 1, 10, 0, 0);
        var candidates = new[] { Candidate(7, planned), Candidate(7, planned) };

        var result = OccurrenceDeduplicator.RemoveExisting(candidates, Array.Empty<Occurrence>());

        result.Should().ContainSingle();
    }

    [Fact]
    public void RemoveExisting_ShouldKeepDistinctSourceOrTime()
    {
        var planned = new DateTime(2026, 3, 1, 10, 0, 0);
        var candidates = new[]
        {
            Candidate(7, planned),           // 与库中重复 → 剔除
            Candidate(8, planned),           // 来源不同 → 保留
            Candidate(7, planned.AddMinutes(1)), // 时刻不同 → 保留
        };
        var existing = new[] { Existing(7, planned) };

        var result = OccurrenceDeduplicator.RemoveExisting(candidates, existing);

        result.Should().HaveCount(2);
        result.Select(r => r.SourceId).Should().BeEquivalentTo(new[] { 8, 7 });
    }

    [Fact]
    public void RemoveExisting_ShouldPreserveInputOrder()
    {
        var candidates = new[]
        {
            Candidate(2, new DateTime(2026, 3, 1, 8, 0, 0)),
            Candidate(1, new DateTime(2026, 3, 1, 9, 0, 0)),
            Candidate(3, new DateTime(2026, 3, 1, 10, 0, 0)),
        };

        var result = OccurrenceDeduplicator.RemoveExisting(candidates, Array.Empty<Occurrence>());

        result.Select(r => r.SourceId).Should().ContainInOrder(2, 1, 3);
    }
}
