using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Time;

namespace FuShi.Tests;

/// <summary>
/// TimeZoneResolver 的单元测试，重点覆盖英国夏令时切换的两类边界：
/// 3 月「跳过的一小时」（无效时刻）与 10 月「重复的一小时」（歧义时刻）。
/// </summary>
public class TimeZoneResolverTests
{
    /// <summary>英国 2026 年夏令时开始日（3 月最后一个周日）：本地 01:00→02:00，01:00-01:59 不存在。</summary>
    private static readonly DateTime DST_START_INVALID_TIME = new(2026, 3, 29, 1, 30, 0);

    /// <summary>英国 2026 年夏令时结束日（10 月最后一个周日）：本地 02:00→01:00，01:00-01:59 出现两次。</summary>
    private static readonly DateTime DST_END_AMBIGUOUS_TIME = new(2026, 10, 25, 1, 30, 0);

    private static TimeZoneInfo London => TimeZoneResolver.Resolve(TimeDefaults.DEFAULT_TIMEZONE_ID).Zone;

    [Fact]
    public void Resolve_ShouldRecognizeDefaultTimeZoneWithDaylightSaving()
    {
        var lookup = TimeZoneResolver.Resolve(TimeDefaults.DEFAULT_TIMEZONE_ID);

        lookup.UsedFallback.Should().BeFalse("欧洲/伦敦应能被直接识别");
        lookup.Zone.SupportsDaylightSavingTime.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldFallBackToUtc_WhenIdUnknown()
    {
        var lookup = TimeZoneResolver.Resolve("Not/AZone");

        lookup.UsedFallback.Should().BeTrue();
        lookup.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void Normalize_ShouldShiftInvalidTimeToFirstValidMoment()
    {
        // 前置：确认该时刻在伦敦确属「被跳过的一小时」。
        London.IsInvalidTime(DST_START_INVALID_TIME).Should().BeTrue();

        var resolution = TimeZoneResolver.Normalize(DST_START_INVALID_TIME, London);

        resolution.Kind.Should().Be(DstAdjustmentKind.InvalidShiftedForward);
        resolution.LocalTime.Should().Be(new DateTime(2026, 3, 29, 2, 0, 0));
    }

    [Fact]
    public void Normalize_ShouldKeepWallClockAndFlagFirst_ForAmbiguousTime()
    {
        // 前置：确认该时刻在伦敦确属「重复的一小时」。
        London.IsAmbiguousTime(DST_END_AMBIGUOUS_TIME).Should().BeTrue();

        var resolution = TimeZoneResolver.Normalize(DST_END_AMBIGUOUS_TIME, London);

        resolution.Kind.Should().Be(DstAdjustmentKind.AmbiguousPickedFirst);
        resolution.LocalTime.Should().Be(DST_END_AMBIGUOUS_TIME);
    }

    [Fact]
    public void Normalize_ShouldLeaveOrdinaryTimeUntouched()
    {
        var ordinary = new DateTime(2026, 6, 15, 19, 45, 0);

        var resolution = TimeZoneResolver.Normalize(ordinary, London);

        resolution.Kind.Should().Be(DstAdjustmentKind.None);
        resolution.LocalTime.Should().Be(ordinary);
    }
}
