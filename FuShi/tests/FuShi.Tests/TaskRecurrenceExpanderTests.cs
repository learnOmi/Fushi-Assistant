using FluentAssertions;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Domain.Time;

namespace FuShi.Tests;

/// <summary>
/// 事项展开的纯算法测试（M4c）：三档重复、区间裁剪、状态与截止为空的边界。
/// 全部用固定日期与 UTC 时区，不依赖跑测试时的系统时钟。
/// </summary>
public sealed class TaskRecurrenceExpanderTests
{
    private static readonly DateOnly From = new(2026, 10, 19);
    private static readonly DateOnly To = new(2026, 10, 25);

    [Fact]
    public void Expand_None_ShouldEmitOnceWhenDueInsideWindow()
    {
        var task = NewTask(new DateTime(2026, 10, 21, 9, 0, 0), TaskRecurrenceExpander.RULE_NONE);

        var result = TaskRecurrenceExpander.Expand(task, From, To, TimeZoneInfo.Utc);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].SourceType.Should().Be(OwnerType.Task);
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 10, 21, 9, 0, 0));
        result.Occurrences[0].EndsAt.Should().Be(result.Occurrences[0].PlannedAt, "事项没有时长");
    }

    [Fact]
    public void Expand_None_ShouldBeEmptyWhenDueOutsideWindow()
    {
        var task = NewTask(new DateTime(2026, 10, 26, 9, 0, 0), TaskRecurrenceExpander.RULE_NONE);

        var result = TaskRecurrenceExpander.Expand(task, From, To, TimeZoneInfo.Utc);

        result.Occurrences.Should().BeEmpty();
    }

    [Fact]
    public void Expand_Daily_ShouldEmitOnePerDayInsideWindow()
    {
        var task = NewTask(new DateTime(2026, 10, 20, 8, 30, 0), TaskRecurrenceExpander.RULE_DAILY);

        var result = TaskRecurrenceExpander.Expand(task, From, To, TimeZoneInfo.Utc);

        result.Occurrences.Should().HaveCount(6, "20~25 日共 6 天");
        result.Occurrences.Select(o => o.PlannedAt).Should().BeInAscendingOrder();
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 10, 20, 8, 30, 0));
        result.Occurrences[^1].PlannedAt.Should().Be(new DateTime(2026, 10, 25, 8, 30, 0));
    }

    [Fact]
    public void Expand_Weekly_ShouldStepSevenDaysFromDueDate()
    {
        var task = NewTask(new DateTime(2026, 10, 19, 18, 0, 0), TaskRecurrenceExpander.RULE_WEEKLY);

        var result = TaskRecurrenceExpander.Expand(task, From, To, TimeZoneInfo.Utc);

        result.Occurrences.Should().HaveCount(1, "7 天窗口内同一星期几只出现一次");
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 10, 19, 18, 0, 0));

        var wider = TaskRecurrenceExpander.Expand(task, From, From.AddDays(13), TimeZoneInfo.Utc);
        wider.Occurrences.Should().HaveCount(2);
        wider.Occurrences[1].PlannedAt.Should().Be(new DateTime(2026, 10, 26, 18, 0, 0));
    }

    [Fact]
    public void Expand_ShouldSkipDoneOrSkippedTasksAndTasksWithoutDue()
    {
        var done = NewTask(new DateTime(2026, 10, 21, 9, 0, 0), TaskRecurrenceExpander.RULE_DAILY, TaskItemStatus.Done);
        var skipped = NewTask(new DateTime(2026, 10, 21, 9, 0, 0), TaskRecurrenceExpander.RULE_DAILY, TaskItemStatus.Skipped);
        var noDue = NewTask(null, TaskRecurrenceExpander.RULE_DAILY);

        TaskRecurrenceExpander.Expand(done, From, To, TimeZoneInfo.Utc).Occurrences.Should().BeEmpty();
        TaskRecurrenceExpander.Expand(skipped, From, To, TimeZoneInfo.Utc).Occurrences.Should().BeEmpty();
        TaskRecurrenceExpander.Expand(noDue, From, To, TimeZoneInfo.Utc).Occurrences.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, TaskRecurrenceExpander.RULE_NONE)]
    [InlineData("", TaskRecurrenceExpander.RULE_NONE)]
    [InlineData("  daily ", TaskRecurrenceExpander.RULE_DAILY)]
    [InlineData("Weekly", TaskRecurrenceExpander.RULE_WEEKLY)]
    [InlineData("FREQ=DAILY", TaskRecurrenceExpander.RULE_NONE)]
    public void NormalizeRule_ShouldOnlyAcceptControlledValues(string? rule, string expected)
    {
        TaskRecurrenceExpander.NormalizeRule(rule).Should().Be(expected);
    }

    [Fact]
    public void Expand_ShouldNormalizeInvalidDstTime()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        // 2026-03-29 英国进入夏令时：01:00~01:59 不存在，归一化后应落到 02:00。
        var task = NewTask(new DateTime(2026, 3, 29, 1, 30, 0), TaskRecurrenceExpander.RULE_NONE);

        var result = TaskRecurrenceExpander.Expand(
            task,
            new DateOnly(2026, 3, 29),
            new DateOnly(2026, 3, 29),
            london);

        result.Occurrences.Should().ContainSingle();
        result.Occurrences[0].PlannedAt.Should().Be(new DateTime(2026, 3, 29, 2, 0, 0));
        result.Adjustments.Should().ContainSingle(a => a.Kind == DstAdjustmentKind.InvalidShiftedForward);
    }

    private static TaskItem NewTask(DateTime? dueAt, string? rule, TaskItemStatus status = TaskItemStatus.Pending) =>
        new()
        {
            Id = 7,
            Title = "交作业",
            DueAt = dueAt,
            RepeatRule = rule,
            Status = status,
        };
}
