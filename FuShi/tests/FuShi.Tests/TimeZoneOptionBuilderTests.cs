using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Time;

namespace FuShi.Tests;

/// <summary>
/// TimeZoneOptionBuilder 的单元测试。
/// 覆盖设置页「时区下拉空白」的根因：Windows 枚举给的是 Windows ID，
/// 项目存的是 IANA ID，候选集里必须显式补上默认时区，否则下拉选中项为空。
/// </summary>
public class TimeZoneOptionBuilderTests
{
    [Fact]
    public void Build_ShouldPutDefaultTimeZoneFirst()
    {
        var options = TimeZoneOptionBuilder.Build(TimeDefaults.DEFAULT_TIMEZONE_ID);

        options.Should().NotBeEmpty();
        options[0].Should().Be(TimeDefaults.DEFAULT_TIMEZONE_ID, "默认时区必须排在首位，用户一打开就能看到自己的时区");
    }

    [Fact]
    public void Build_ShouldContainDefaultTimeZoneExactlyOnce()
    {
        var options = TimeZoneOptionBuilder.Build(TimeDefaults.DEFAULT_TIMEZONE_ID);

        options.Count(id => id == TimeDefaults.DEFAULT_TIMEZONE_ID)
            .Should().Be(1, "去重后只应出现一次，避免下拉里出现两条同名项");
    }

    [Fact]
    public void Build_ShouldKeepEverySystemTimeZone()
    {
        var options = TimeZoneOptionBuilder.Build(TimeDefaults.DEFAULT_TIMEZONE_ID);
        var systemIds = TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id);

        options.Should().Contain(systemIds, "补默认时区不能丢掉系统枚举出来的时区");
    }

    [Fact]
    public void Build_ShouldNotMutateWhenDefaultIsNotInSystemList()
    {
        // 用一个系统枚举里必然不存在的 ID 模拟「IANA Id 不在候选集」的现状。
        const string missingId = "Mars/Olympus_Mons";

        var options = TimeZoneOptionBuilder.Build(missingId);

        options[0].Should().Be(missingId);
        options.Count(id => id == missingId).Should().Be(1);
        options.Should().HaveCount(TimeZoneInfo.GetSystemTimeZones().Count + 1, "缺少时必须补一条，而不是覆盖系统项");
    }
}
