using FluentAssertions;
using FuShi.Domain.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// ScheduleTimeline 的单元测试：当前/下一节/再下一节的取景，以及进度百分比的边界。
/// </summary>
public class ScheduleTimelineTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0);

    /// <summary>构造一个时段；颜色与备注给固定值即可（取景逻辑不关心）。</summary>
    private static TimelineSlot Slot(DateTime start, DateTime end, int subjectId = 1, string name = "数学") =>
        new(start, end, subjectId, name, "#FF8A5B", null);

    [Fact]
    public void Select_ShouldReturnCurrentNextFollowing()
    {
        var slots = new[]
        {
            Slot(Now.AddHours(-1), Now.AddHours(1), 1, "数学"),  // 当前
            Slot(Now.AddHours(2), Now.AddHours(3), 2, "英语"),  // 下一节
            Slot(Now.AddHours(4), Now.AddHours(5), 3, "政治"),  // 再下一节
            Slot(Now.AddHours(6), Now.AddHours(7), 4, "专业课"),
        };

        var view = ScheduleTimeline.Select(slots, Now);

        view.Current!.SubjectName.Should().Be("数学");
        view.Next!.SubjectName.Should().Be("英语");
        view.Following!.SubjectName.Should().Be("政治");
    }

    [Fact]
    public void Select_ShouldTreatStartBoundaryAsCurrent()
    {
        // 开始时刻恰好等于 now → 视为当前（左闭）。
        var slots = new[] { Slot(Now, Now.AddHours(1)) };

        var view = ScheduleTimeline.Select(slots, Now);

        view.Current.Should().NotBeNull();
        view.Next.Should().BeNull();
    }

    [Fact]
    public void Select_ShouldTreatEndBoundaryAsNotCurrent()
    {
        // 结束时刻恰好等于 now → 不再视为当前（右开），应归入过去。
        var slots = new[] { Slot(Now.AddHours(-1), Now) };

        var view = ScheduleTimeline.Select(slots, Now);

        view.Current.Should().BeNull();
        view.Next.Should().BeNull();
        view.Following.Should().BeNull();
    }

    [Fact]
    public void Select_ShouldSupportCrossMidnightSlot()
    {
        // 跨午夜：昨天 23:00 开始、今天 01:00 结束，now = 00:30 → 仍应视为当前。
        var slots = new[] { Slot(Now.AddDays(-1).Date.AddHours(23), Now.Date.AddHours(1)) };

        var view = ScheduleTimeline.Select(slots, new DateTime(2026, 6, 15, 0, 30, 0));

        view.Current.Should().NotBeNull();
    }

    [Fact]
    public void Select_ShouldFocusNextWhenNothingIsCurrent()
    {
        var slots = new[]
        {
            Slot(Now.AddHours(2), Now.AddHours(3), 2, "英语"),
            Slot(Now.AddHours(4), Now.AddHours(5), 3, "政治"),
        };

        var view = ScheduleTimeline.Select(slots, Now);

        view.Current.Should().BeNull();
        view.Next!.SubjectName.Should().Be("英语");
        view.Following!.SubjectName.Should().Be("政治");
    }

    [Fact]
    public void Select_ShouldReturnAllNullWhenNoSlots()
    {
        var view = ScheduleTimeline.Select(Array.Empty<TimelineSlot>(), Now);

        view.Current.Should().BeNull();
        view.Next.Should().BeNull();
        view.Following.Should().BeNull();
    }

    [Fact]
    public void GetProgressPercent_ShouldBeZeroAtStart()
    {
        var slot = Slot(Now, Now.AddHours(1));

        ScheduleTimeline.GetProgressPercent(slot, Now).Should().Be(0);
    }

    [Fact]
    public void GetProgressPercent_ShouldBeHalfAtMidpoint()
    {
        var slot = Slot(Now, Now.AddHours(1));

        ScheduleTimeline.GetProgressPercent(slot, Now.AddMinutes(30)).Should().BeApproximately(50, 0.001);
    }

    [Fact]
    public void GetProgressPercent_ShouldClampToBoundsOutsideSlot()
    {
        var slot = Slot(Now, Now.AddHours(1));

        ScheduleTimeline.GetProgressPercent(slot, Now.AddMinutes(-10)).Should().Be(0);
        ScheduleTimeline.GetProgressPercent(slot, Now.AddHours(2)).Should().Be(100);
    }

    [Fact]
    public void GetProgressPercent_ShouldReturnZeroForZeroLengthSlot()
    {
        var slot = Slot(Now, Now);

        ScheduleTimeline.GetProgressPercent(slot, Now).Should().Be(0);
    }
}
