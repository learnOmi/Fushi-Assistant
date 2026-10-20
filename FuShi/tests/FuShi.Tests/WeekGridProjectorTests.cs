using FluentAssertions;
using FuShi.Domain.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// 周网格投影的纯算法单测：覆盖空课表、行排序、列映射（周一打头）、跨午夜归属、当前高亮。
/// </summary>
public sealed class WeekGridProjectorTests
{
    [Fact]
    public void Project_ShouldReturnEmptyWhenNoSources()
    {
        var grid = WeekGridProjector.Project(Array.Empty<WeekGridSource>(), DateTime.Now);

        grid.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Project_ShouldCreateOneRowPerDistinctStartTimeSortedAscending()
    {
        var sources = new[]
        {
            Source(1, DayOfWeek.Monday, new TimeOnly(19, 45), new TimeOnly(21, 0)),
            Source(2, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0)),
            Source(3, DayOfWeek.Wednesday, new TimeOnly(19, 45), new TimeOnly(21, 0)),
        };

        var grid = WeekGridProjector.Project(sources, DateTime.Now);

        grid.Rows.Should().HaveCount(2, "去重后只有 09:00 与 19:45 两个起始时刻");
        grid.Rows[0].StartTime.Should().Be(new TimeOnly(9, 0));
        grid.Rows[1].StartTime.Should().Be(new TimeOnly(19, 45));
    }

    [Fact]
    public void Project_ShouldMapColumnsMondayFirst()
    {
        var sources = new[] { Source(1, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0)) };

        var grid = WeekGridProjector.Project(sources, DateTime.Now);
        var row = grid.Rows.Single();

        row.Cells.Should().HaveCount(7);
        row.Cells[0].DayOfWeek.Should().Be(DayOfWeek.Monday, "第 0 列是周一");
        row.Cells[6].DayOfWeek.Should().Be(DayOfWeek.Sunday, "第 6 列是周日");
        row.Cells[0].Items.Should().ContainSingle();
        row.Cells[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Project_ShouldMarkCrossMidnightAndKeepItOnStartDay()
    {
        // 周二 23:00 → 周三 01:00，归「周二」那一格。
        var sources = new[] { Source(1, DayOfWeek.Tuesday, new TimeOnly(23, 0), new TimeOnly(1, 0)) };

        var grid = WeekGridProjector.Project(sources, DateTime.Now);
        var row = grid.Rows.Single();

        var item = row.Cells[1].Items.Single(); // 索引 1 = 周二
        item.IsCrossMidnight.Should().BeTrue();
        row.Cells[2].IsEmpty.Should().BeTrue("跨午夜不拆到次日那一格");
    }

    [Fact]
    public void Project_ShouldHighlightCurrentSlotOnMatchingWeekday()
    {
        // 找一个确定落在「周二 10:00」的 now：取本周内的某个周二，简化用固定日期拼装。
        var tuesday = NextDateOf(DayOfWeek.Tuesday);
        var now = tuesday.AddHours(10); // 周二 10:00

        var sources = new[]
        {
            Source(1, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(11, 0)),   // 覆盖 10:00 → 高亮
            Source(2, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(11, 0)), // 别天 → 不高亮
        };

        var grid = WeekGridProjector.Project(sources, now);

        grid.Rows[0].Cells[1].Items.Single().IsCurrent.Should().BeTrue();  // 周二
        grid.Rows[0].Cells[2].Items.Single().IsCurrent.Should().BeFalse(); // 周三
    }

    [Fact]
    public void Project_ShouldHighlightCrossMidnightInNextDayEarlyMorning()
    {
        // 跨午夜条目：周二 23:00 → 周三 01:00；在「周三 00:30」应高亮。
        var wednesday = NextDateOf(DayOfWeek.Wednesday);
        var now = wednesday.AddMinutes(30); // 周三 00:30

        var sources = new[] { Source(1, DayOfWeek.Tuesday, new TimeOnly(23, 0), new TimeOnly(1, 0)) };

        var grid = WeekGridProjector.Project(sources, now);

        grid.Rows.Single().Cells[1].Items.Single().IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Project_ShouldCarryDisabledFlag()
    {
        var sources = new[] { Source(1, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), enabled: false) };

        var grid = WeekGridProjector.Project(sources, DateTime.Now);

        grid.Rows.Single().Cells[0].Items.Single().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Project_ShouldApplyShiftOnlyOnTodayColumn_AndKeepRowTime()
    {
        // 取一个确定的「今天」= 周二 08:00。
        var now = NextDateOf(DayOfWeek.Tuesday).AddHours(8);

        var sources = new[]
        {
            Source(1, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), shiftMinutes: 10),   // 今天 → 叠顺延
            Source(2, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(10, 0), shiftMinutes: 10), // 别天 → 不叠
        };

        var grid = WeekGridProjector.Project(sources, now);
        var row = grid.Rows.Single(); // 两条同起始时刻 09:00，去重成一行

        row.StartTime.Should().Be(new TimeOnly(9, 0), "行仍按模板起始时刻排列，块不搬家");

        var todayItem = row.Cells[1].Items.Single(); // 索引 1 = 周二 = 今天
        todayItem.StartTime.Should().Be(new TimeOnly(9, 10), "今天这一列显示顺延后的时段");
        todayItem.EndTime.Should().Be(new TimeOnly(10, 10), "结束时刻整块同长顺移");
        todayItem.IsShifted.Should().BeTrue("顺延过 → 点亮「已顺延」角标");

        var otherDayItem = row.Cells[2].Items.Single(); // 索引 2 = 周三
        otherDayItem.StartTime.Should().Be(new TimeOnly(9, 0), "其余六天必须保持模板时间，否则就骗人了");
        otherDayItem.IsShifted.Should().BeFalse();
    }

    [Fact]
    public void Project_ShouldMarkSkippedOnlyOnTodayColumn()
    {
        // 取一个确定的「今天」= 周二 08:00。
        var now = NextDateOf(DayOfWeek.Tuesday).AddHours(8);

        var sources = new[]
        {
            Source(1, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), isSkipped: true),   // 今天 → 显示「已跳过」
            Source(2, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(10, 0), isSkipped: true), // 别天 → 不显示
        };

        var grid = WeekGridProjector.Project(sources, now);
        var row = grid.Rows.Single(); // 两条同起始时刻 09:00，去重成一行

        row.Cells[1].Items.Single().IsSkipped.Should().BeTrue("今天这一列被跳过 → 点亮「已跳过」角标");
        row.Cells[2].Items.Single().IsSkipped.Should().BeFalse("其余六天不叠跳过，否则就骗人了");
    }

    /// <summary>构造一条模板来源。</summary>
    private static WeekGridSource Source(
        int id,
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        bool enabled = true,
        int shiftMinutes = 0,
        bool isSkipped = false) =>
        new(id, day, start, end, "数学", "#FF8A5B", null, enabled, shiftMinutes, isSkipped);

    /// <summary>取从今天起的下一个指定星期几（若今天就是，返回今天）。</summary>
    private static DateTime NextDateOf(DayOfWeek target)
    {
        var date = DateTime.Today;
        while (date.DayOfWeek != target)
        {
            date = date.AddDays(1);
        }

        return date;
    }
}
