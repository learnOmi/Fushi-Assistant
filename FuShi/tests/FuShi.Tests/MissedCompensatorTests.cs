using FluentAssertions;
using FuShi.Domain.Compensation;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;

namespace FuShi.Tests;

/// <summary>
/// MissedCompensator 的单元测试：宽限期内补发、超出宽限标记错过、未到点/已处理不动，以及汇总摘要。
/// </summary>
public class MissedCompensatorTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0);
    private static readonly DateTime Since = Now.AddHours(-3);

    /// <summary>宽限期取 30 分钟，便于构造「刚好在窗口内/外」的用例。</summary>
    private const int GRACE_MINUTES = 30;

    private static Occurrence Pending(int id, DateTime plannedAt, OccurrenceStatus status = OccurrenceStatus.Pending,
        DateTime? firedAt = null) => new()
    {
        Id = id,
        SourceType = OwnerType.ScheduleEntry,
        SourceId = id,
        PlannedAt = plannedAt,
        Status = status,
        FiredAt = firedAt,
    };

    [Fact]
    public void Evaluate_ShouldResendWithinGracePeriod()
    {
        // 计划 11:45，距 now 15 分钟，落在 30 分钟宽限内 → 补发。
        var occurrence = Pending(1, Now.AddMinutes(-15));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().ContainSingle().Which.Id.Should().Be(1);
        result.ToMarkMissed.Should().BeEmpty();
        result.SummaryText.Should().BeNull();
    }

    [Fact]
    public void Evaluate_ShouldMarkMissedBeyondGracePeriod()
    {
        // 计划 10:00，距 now 2 小时，超出宽限 → 标记错过并生成摘要。
        var occurrence = Pending(2, Now.AddHours(-2));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().BeEmpty();
        result.ToMarkMissed.Should().ContainSingle().Which.Id.Should().Be(2);
        result.SummaryText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Evaluate_ShouldIgnoreFutureOccurrences()
    {
        var occurrence = Pending(3, Now.AddMinutes(10));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().BeEmpty();
        result.ToMarkMissed.Should().BeEmpty();
        result.SummaryText.Should().BeNull();
    }

    [Fact]
    public void Evaluate_ShouldIgnoreAlreadyFiredOccurrences()
    {
        var occurrence = Pending(4, Now.AddMinutes(-5), firedAt: Now.AddMinutes(-4));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().BeEmpty();
        result.ToMarkMissed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OccurrenceStatus.Done)]
    [InlineData(OccurrenceStatus.Skipped)]
    [InlineData(OccurrenceStatus.Missed)]
    public void Evaluate_ShouldIgnoreTerminalStates(OccurrenceStatus status)
    {
        var occurrence = Pending(5, Now.AddMinutes(-5), status: status);

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().BeEmpty();
        result.ToMarkMissed.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldIgnoreOccurrencesBeforeScanWindow()
    {
        // 计划时刻早于 since（本轮窗口之前，已在上轮处理）→ 不动。
        var occurrence = Pending(6, Since.AddMinutes(-5));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().BeEmpty();
        result.ToMarkMissed.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldMergeMissedIntoOneSummary()
    {
        var occurrences = new[]
        {
            Pending(7, Now.AddHours(-1)),
            Pending(8, Now.AddHours(-2)),
            Pending(9, Now.AddMinutes(-10)), // 宽限内，应补发而非错过
        };

        var result = MissedCompensator.Evaluate(occurrences, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().ContainSingle().Which.Id.Should().Be(9);
        result.ToMarkMissed.Should().HaveCount(2);
        result.SummaryText.Should().Contain("2 条").And.Contain("3 小时");
    }

    [Fact]
    public void Evaluate_ShouldTreatGraceBoundaryAsResend()
    {
        // 恰好落在宽限边界（now - 30min）→ 按补发处理（闭区间）。
        var occurrence = Pending(10, Now.AddMinutes(-GRACE_MINUTES));

        var result = MissedCompensator.Evaluate(new[] { occurrence }, Now, Since, GRACE_MINUTES);

        result.ToResend.Should().ContainSingle();
        result.ToMarkMissed.Should().BeEmpty();
    }
}
