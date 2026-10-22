using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using FuShi.Infrastructure.Tasks;

namespace FuShi.Tests;

/// <summary>
/// M4c 集成测试：事项的展开落库、编辑对账、完成/跳过收尾、提醒响应（完成/稍后）与每物主提前量。
/// 与 ReminderResponseServiceTests 同一套路——在真实临时 SQLite 库上跑完整数据链。
/// </summary>
public sealed class TaskServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ScheduleService _schedule;
    private readonly TaskService _tasks;
    private readonly ReminderResponseService _response;

    /// <summary>统一基准时刻：今天 08:00，避开「今天 09:00」的截止点，便于断言未来实例。</summary>
    private readonly DateTime _now = DateTime.Today.AddHours(8);

    public TaskServiceTests()
    {
        var settings = new AppSettingRepository(_db.Factory);
        _schedule = new ScheduleService(_db.Factory, settings, _db.Logger);
        _tasks = new TaskService(_db.Factory, _schedule, _db.Logger);
        _response = new ReminderResponseService(_db.Factory, _db.Logger);
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_ShouldMaterializeOccurrence_AndBeVisibleToDueScan()
    {
        var due = DateTime.Today.AddDays(1).AddHours(9);
        var task = await _tasks.CreateAsync(NewTask("交作业", due, TaskRecurrenceExpander.RULE_NONE), _now);

        using (var context = _db.CreateContext())
        {
            var occurrence = context.Occurrences.Single(o =>
                o.SourceType == OwnerType.Task && o.SourceId == task.Id);

            occurrence.PlannedAt.Should().Be(due, "NONE 在截止当天出一次");
            occurrence.Status.Should().Be(OccurrenceStatus.Pending);
        }

        var dueRows = await _schedule.GetDueAsync(due.AddMinutes(-1), due.AddMinutes(1));
        var row = dueRows.Should().ContainSingle(r => r.Occurrence.SourceId == task.Id).Subject;
        row.IsTask.Should().BeTrue();
        row.SubjectName.Should().Be("交作业", "通知标题取事项标题");
        row.EndsAt.Should().Be(due, "事项没有时长，结束时刻 = 截止时刻");
        row.TriggerAt.Should().Be(due, "无提前量时触发点就是截止时刻");
    }

    [Fact]
    public async Task UpdateDueDate_ShouldReplaceFutureOccurrenceOnly()
    {
        var task = await _tasks.CreateAsync(
            NewTask("写周报", DateTime.Today.AddDays(1).AddHours(9), TaskRecurrenceExpander.RULE_NONE),
            _now);

        var newDue = DateTime.Today.AddDays(2).AddHours(10);
        task.DueAt = newDue;
        (await _tasks.UpdateAsync(task, _now)).Should().BeTrue();

        using var context = _db.CreateContext();
        var occurrences = context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task && o.SourceId == task.Id)
            .ToList();

        occurrences.Should().ContainSingle("旧截止的未来实例应被对账清掉，只留新截止的一条");
        occurrences[0].PlannedAt.Should().Be(newDue);
    }

    [Fact]
    public async Task MarkDone_ShouldLogCompletion_AndStopFutureReminders()
    {
        var task = await _tasks.CreateAsync(
            NewTask("背单词", DateTime.Today.AddHours(9), TaskRecurrenceExpander.RULE_DAILY),
            _now);

        (await _tasks.MarkDoneAsync(task.Id, _now.AddMinutes(30))).Should().BeTrue();

        using var context = _db.CreateContext();
        context.Tasks.Single(t => t.Id == task.Id).Status.Should().Be(TaskItemStatus.Done);

        var occurrences = context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task && o.SourceId == task.Id)
            .ToList();

        occurrences.Should().ContainSingle("今天这条置为完成，明天及以后不再提醒");
        occurrences[0].Status.Should().Be(OccurrenceStatus.Done);

        var log = context.CompletionLogs.Single();
        log.EntryId.Should().Be(task.Id);
        log.Date.Should().Be(DateOnly.FromDateTime(DateTime.Today));
        log.Status.Should().Be(CompletionStatus.Done);
        log.SubjectId.Should().BeNull("事项没有科目，统计归入未分类");
        log.PlannedMin.Should().Be(0);
        log.ActualMin.Should().Be(0);
    }

    [Fact]
    public async Task MarkSkipped_ShouldSetSkippedAndClearFuture()
    {
        var task = await _tasks.CreateAsync(
            NewTask("刷题", DateTime.Today.AddHours(9), TaskRecurrenceExpander.RULE_WEEKLY),
            _now);

        (await _tasks.MarkSkippedAsync(task.Id, _now)).Should().BeTrue();

        using var context = _db.CreateContext();
        context.Tasks.Single(t => t.Id == task.Id).Status.Should().Be(TaskItemStatus.Skipped);
        context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task && o.SourceId == task.Id && o.Status == OccurrenceStatus.Pending)
            .Should().BeEmpty("跳过后不再有未来待触发实例");
        context.CompletionLogs.Single().Status.Should().Be(CompletionStatus.Skipped);
    }

    [Fact]
    public async Task Delete_ShouldRemoveTaskAndPendingOccurrences_ButKeepHistory()
    {
        var task = await _tasks.CreateAsync(
            NewTask("交作业", DateTime.Today.AddHours(9), TaskRecurrenceExpander.RULE_NONE),
            _now);
        await _tasks.MarkDoneAsync(task.Id, _now.AddMinutes(30));

        (await _tasks.DeleteAsync(task.Id)).Should().BeTrue();

        using var context = _db.CreateContext();
        context.Tasks.Should().BeEmpty();
        context.Occurrences
            .Where(o => o.Status == OccurrenceStatus.Pending)
            .Should().BeEmpty("未来待触发实例随事项一起清掉");
        context.CompletionLogs.Should().ContainSingle("历史完成记录不受删除影响");
    }

    [Fact]
    public async Task GetTasks_ShouldIncludeOverduePending_ButNotOldCompleted()
    {
        var yesterday = DateTime.Today.AddDays(-1).AddHours(9);
        var yesTask = await _tasks.CreateAsync(
            NewTask("逾期未做", yesterday, TaskRecurrenceExpander.RULE_NONE), _now);
        var doneTask = await _tasks.CreateAsync(
            NewTask("昨天已完成", yesterday, TaskRecurrenceExpander.RULE_NONE), _now);
        await _tasks.MarkDoneAsync(doneTask.Id, _now);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var list = await _tasks.GetTasksAsync(today, today.AddDays(6));

        list.Should().Contain(t => t.Id == yesTask.Id, "逾期未完成的事项永远留在列表里");
        list.Should().NotContain(t => t.Id == doneTask.Id, "区间外的已完成事项不再展示");
    }

    [Fact]
    public async Task ReminderRule_ShouldOverrideGlobalLead_ForDueScan()
    {
        var due = DateTime.Today.AddHours(18);
        var task = await _tasks.CreateAsync(
            NewTask("交作业", due, TaskRecurrenceExpander.RULE_NONE), _now);

        using (var context = _db.CreateContext())
        {
            context.ReminderRules.Add(new ReminderRule
            {
                OwnerType = OwnerType.Task,
                OwnerId = task.Id,
                TriggerType = TriggerType.BeforeStart,
                OffsetMinutes = 30,
            });
            context.SaveChanges();
        }

        var triggerAt = due.AddMinutes(-30);
        var rows = await _schedule.GetDueAsync(triggerAt.AddMinutes(-1), triggerAt.AddMinutes(1));

        var row = rows.Should().ContainSingle(r => r.Occurrence.SourceId == task.Id).Subject;
        row.LeadMinutes.Should().Be(30, "事项自己的规则优先于全局提前量");
        row.TriggerAt.Should().Be(triggerAt);
    }

    [Fact]
    public async Task RespondFinish_OnTaskOccurrence_ShouldCloseTaskAndLogWithoutMinutes()
    {
        var due = DateTime.Today.AddHours(9);
        var task = await _tasks.CreateAsync(
            NewTask("背单词", due, TaskRecurrenceExpander.RULE_DAILY), _now);

        int occurrenceId;
        using (var context = _db.CreateContext())
        {
            occurrenceId = context.Occurrences
                .Where(o => o.SourceType == OwnerType.Task && o.SourceId == task.Id)
                .OrderBy(o => o.PlannedAt)
                .First()
                .Id;
        }

        await _schedule.MarkFiredAsync(occurrenceId, due);
        await _response.RespondFinishAsync(occurrenceId, due.AddMinutes(3));

        using var read = _db.CreateContext();
        read.Tasks.Single(t => t.Id == task.Id).Status.Should().Be(TaskItemStatus.Done);
        read.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == task.Id
                        && o.PlannedAt > due)
            .Should().BeEmpty("重复事项完成一次后，后续次数不再提醒");

        var log = read.CompletionLogs.Single();
        log.EntryId.Should().Be(task.Id);
        log.SubjectId.Should().BeNull("事项 Id 可能撞上课表条目 Id，不能误取科目");
        log.PlannedMin.Should().Be(0);
        log.ActualMin.Should().Be(0, "事项没有实际耗时语义");
    }

    [Fact]
    public async Task RespondSnooze_OnTask_ShouldShiftWithinDay_AndRefuseCrossingMidnight()
    {
        var due = DateTime.Today.AddHours(9);
        var task = await _tasks.CreateAsync(
            NewTask("交作业", due, TaskRecurrenceExpander.RULE_NONE), _now);
        int occurrenceId;
        using (var context = _db.CreateContext())
        {
            occurrenceId = context.Occurrences.Single(o => o.SourceId == task.Id).Id;
        }

        await _schedule.MarkFiredAsync(occurrenceId, due);
        await _response.RespondSnoozeAsync(occurrenceId, due);

        using (var context = _db.CreateContext())
        {
            var occurrence = context.Occurrences.Single(o => o.Id == occurrenceId);
            occurrence.ShiftMinutes.Should().Be(TimeDefaults.DEFAULT_SNOOZE_MINUTES);
            occurrence.Status.Should().Be(OccurrenceStatus.Pending);
            occurrence.FiredAt.Should().BeNull("回退待触发，新触发点才能重弹");
        }

        // 23:55 再顺延 10 分钟会越过次日 00:00：必须拒绝，且不改动已有顺延。
        var lateDue = DateTime.Today.AddHours(23).AddMinutes(55);
        var lateTask = await _tasks.CreateAsync(
            NewTask("睡前复盘", lateDue, TaskRecurrenceExpander.RULE_NONE), _now);
        int lateOccurrenceId;
        using (var context = _db.CreateContext())
        {
            lateOccurrenceId = context.Occurrences.Single(o => o.SourceId == lateTask.Id).Id;
        }

        await _schedule.MarkFiredAsync(lateOccurrenceId, lateDue);
        await _response.RespondSnoozeAsync(lateOccurrenceId, lateDue);

        using (var context = _db.CreateContext())
        {
            var occurrence = context.Occurrences.Single(o => o.Id == lateOccurrenceId);
            occurrence.ShiftMinutes.Should().Be(0, "禁止跨次日顺延，触顶时保持原样");
            occurrence.Status.Should().Be(OccurrenceStatus.Fired, "不改状态，避免在旧触发点立即重弹");
        }
    }

    /// <summary>构造一条事项模板；重复档位用受控文本。</summary>
    private static TaskItem NewTask(string title, DateTime dueAt, string repeatRule) => new()
    {
        Title = title,
        DueAt = dueAt,
        RepeatRule = repeatRule,
        Priority = TaskPriority.Normal,
    };
}
