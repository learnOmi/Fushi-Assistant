using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// M3b 集成测试：提醒响应服务（开始/稍后/跳过/完成/未响应）的状态迁移与账本写入，
/// 以及它牵动的调度行为——提前量窗口偏移、顺延（当日调课）后的重新排期、InProgress 到 EndsAt 收尾（含跨午夜）。
/// 在真实临时 SQLite 库上跑，覆盖 UI 线程与引擎线程共用的这条数据链。
/// </summary>
public sealed class ReminderResponseServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly AppSettingRepository _settings;
    private readonly ScheduleService _schedule;
    private readonly ReminderResponseService _response;

    public ReminderResponseServiceTests()
    {
        _settings = new AppSettingRepository(_db.Factory);
        _schedule = new ScheduleService(_db.Factory, _settings, _db.Logger);
        _response = new ReminderResponseService(_db.Factory, _db.Logger);
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Start_ShouldMarkInProgress_ThenCompletionScanFiresAtEndsAt_AndFinishLogsDone()
    {
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        await _schedule.MarkFiredAsync(id, plannedAt);

        await _response.RespondStartAsync(id);
        ReadStatus(id).Should().Be(OccurrenceStatus.InProgress);

        var endsAt = plannedAt.Date.Add(new TimeOnly(10, 0).ToTimeSpan());

        // 引擎把休眠睡到该时刻：进行中最早结束时刻 = 10:00；到点后不再返回。
        (await _schedule.GetNextCompletionAtAsync(plannedAt)).Should().Be(endsAt);
        (await _schedule.GetNextCompletionAtAsync(endsAt)).Should().BeNull();

        (await _schedule.GetDueCompletionsAsync(endsAt.AddMinutes(-1)))
            .Should().BeEmpty("未到结束时刻不应自动完成");
        (await _schedule.GetDueCompletionsAsync(endsAt))
            .Should().ContainSingle(o => o.Id == id, "到结束时刻应被完成扫描命中");

        await _response.RespondFinishAsync(id, endsAt);
        ReadStatus(id).Should().Be(OccurrenceStatus.Done);

        var log = ReadSingleCompletionLog();
        log.Status.Should().Be(CompletionStatus.Done);
        log.PlannedMin.Should().Be(60, "模板 09:00-10:00 的计划时长为 60 分钟");
        log.ActualMin.Should().Be(60, "实际时长 = 结束时刻 − FiredAt(09:00)");
        log.Date.Should().Be(DateOnly.FromDateTime(plannedAt));
    }

    [Fact]
    public async Task Snooze_ShouldRevertToPendingAndShift_ThenRefireAtNewTrigger()
    {
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        await _schedule.MarkFiredAsync(id, plannedAt);

        var snoozedAt = plannedAt.Date.Add(new TimeOnly(9, 0).ToTimeSpan());
        await _response.RespondSnoozeAsync(id, snoozedAt);

        using (var context = _db.CreateContext())
        {
            var occurrence = context.Occurrences.Single(o => o.Id == id);
            occurrence.Status.Should().Be(OccurrenceStatus.Pending, "「稍后」把实例回退成待触发");
            occurrence.FiredAt.Should().BeNull("清掉 FiredAt，使新触发点到点可重弹");
            occurrence.ShiftMinutes.Should().Be(TimeDefaults.DEFAULT_SNOOZE_MINUTES, "当日顺延 10 分钟");
            occurrence.ShiftBatchId.Should().NotBeNull("顺延要打批次号，供对账成批回退");
        }

        // 顺延后有效触发点 = 09:10（无全局提前量）：顺延到期前属「未来待触发」
        var upcoming = await _schedule.GetUpcomingAsync(snoozedAt.AddMinutes(5));
        upcoming.Should().ContainSingle(r => r.Occurrence.Id == id, "顺延后的实例必须重新参与调度");
        upcoming[0].TriggerAt.Should().Be(plannedAt.Date.Add(new TimeOnly(9, 10).ToTimeSpan()));

        // 顺延到期窗口内应被「本轮到期」命中
        var due = await _schedule.GetDueAsync(snoozedAt.AddMinutes(9), snoozedAt.AddMinutes(11));
        due.Should().ContainSingle(r => r.Occurrence.Id == id);
    }

    [Fact]
    public async Task Snooze_ShouldChainShiftOverlappingNextCourse()
    {
        // 第一节 09:00-10:00、第二节 10:00-11:00。第一节顺延 10 分钟 → 09:10-10:10 会压到第二节，
        // 于是第二节也被链式推后 10 分钟 → 10:10-11:10，两者共用同一批次号。
        var (firstId, secondId) = await MaterializeTwoCoursesAsync(
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TimeOnly(10, 0), new TimeOnly(11, 0));

        var snoozedAt = DateTime.Today.Add(new TimeOnly(9, 0).ToTimeSpan());
        await _response.RespondSnoozeAsync(firstId, snoozedAt);

        using var context = _db.CreateContext();
        var first = context.Occurrences.Single(o => o.Id == firstId);
        var second = context.Occurrences.Single(o => o.Id == secondId);

        first.ShiftMinutes.Should().Be(TimeDefaults.DEFAULT_SNOOZE_MINUTES, "被点的那条顺延 10 分钟");
        first.Status.Should().Be(OccurrenceStatus.Pending);
        first.FiredAt.Should().BeNull();

        second.ShiftMinutes.Should().Be(TimeDefaults.DEFAULT_SNOOZE_MINUTES, "第一节顺延后压到第二节，链式把第二节也推后");
        second.ShiftBatchId.Should().Be(first.ShiftBatchId, "同一条链共用一个批次号");
        second.Status.Should().Be(OccurrenceStatus.Pending, "配角不改状态，只挪时间");
    }

    [Fact]
    public async Task Skip_ShouldSetSkipped_AndLogSkippedCompletion()
    {
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        await _schedule.MarkFiredAsync(id, plannedAt);

        await _response.RespondSkipAsync(id);

        ReadStatus(id).Should().Be(OccurrenceStatus.Skipped);
        var log = ReadSingleCompletionLog();
        log.Status.Should().Be(CompletionStatus.Skipped);
        log.PlannedMin.Should().Be(60);
        log.ActualMin.Should().Be(0);
    }

    [Fact]
    public async Task NoResponse_ShouldKeepFired_AndLogMissed()
    {
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        await _schedule.MarkFiredAsync(id, plannedAt);

        await _response.RecordNoResponseAsync(id);

        ReadStatus(id).Should().Be(OccurrenceStatus.Fired, "未响应不改变状态，仍为已触发");
        ReadSingleCompletionLog().Status.Should().Be(CompletionStatus.Missed);
    }

    [Fact]
    public async Task CompletionScan_ShouldHandleCrossMidnightEndsAt()
    {
        // 23:00 ~ 次日 00:30 的跨午夜时段：EndTime ⩽ StartTime，结束时刻顺延一天。
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(23, 0), new TimeOnly(0, 30));
        plannedAt.Should().Be(
            plannedAt.Date.Add(new TimeOnly(23, 0).ToTimeSpan()),
            "跨午夜时段归起始日不拆分");

        await _schedule.MarkFiredAsync(id, plannedAt);
        await _response.RespondStartAsync(id);

        var endsAt = plannedAt.Date.AddDays(1).Add(new TimeOnly(0, 30).ToTimeSpan());

        (await _schedule.GetDueCompletionsAsync(endsAt.AddMinutes(-1)))
            .Should().BeEmpty("次日 00:29 还没到结束时刻");
        (await _schedule.GetDueCompletionsAsync(endsAt))
            .Should().ContainSingle(o => o.Id == id, "次日 00:30 才是结束时刻");
    }

    [Fact]
    public async Task LeadTime_ShouldShiftTriggerWindow_WithoutMovingLedgerKey()
    {
        // 提前 5 分钟：写进设置（账本键 PlannedAt 保持不变，只在查询层平移窗口）。
        var setting = await _settings.GetAsync();
        setting.LeadTimeMinutes = 5;
        await _settings.SaveAsync(setting);

        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        plannedAt.Should().Be(plannedAt.Date.Add(new TimeOnly(9, 0).ToTimeSpan()), "提前量不改变账本键");

        // 触发点 = 08:55：窗口 [08:55, 09:01] 应覆盖 PlannedAt(09:00)
        var due = await _schedule.GetDueAsync(
            plannedAt.Date.Add(new TimeOnly(8, 50).ToTimeSpan()),
            plannedAt.Date.Add(new TimeOnly(8, 56).ToTimeSpan()));
        due.Should().ContainSingle(r => r.Occurrence.Id == id);
        due[0].TriggerAt.Should().Be(plannedAt.AddMinutes(-5));

        // 08:56 时它已「到期」，不再属于未来待触发
        (await _schedule.GetUpcomingAsync(plannedAt.Date.Add(new TimeOnly(8, 56).ToTimeSpan())))
            .Should().BeEmpty();

        // 08:50 时它仍是未来项，触发点为 08:55
        var earlier = await _schedule.GetUpcomingAsync(plannedAt.Date.Add(new TimeOnly(8, 50).ToTimeSpan()));
        earlier.Should().ContainSingle(r => r.Occurrence.Id == id);
        earlier[0].TriggerAt.Should().Be(plannedAt.AddMinutes(-5));
    }

    [Fact]
    public async Task Finish_ShouldSnapshotCurrentSubjectId()
    {
        var (id, plannedAt) = await MaterializeTodayAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        await _schedule.MarkFiredAsync(id, plannedAt);

        await _response.RespondFinishAsync(id, plannedAt.AddMinutes(45));

        int subjectId;
        using (var context = _db.CreateContext())
        {
            subjectId = context.Subjects.Single().Id;
        }

        ReadSingleCompletionLog().SubjectId.Should().Be(subjectId, "落账时写入当时的科目快照（M4b）");
    }

    // ---------- 测试辅助 ----------

    /// <summary>种一条「今天生效、今天有课」的最小课表，展开后返回唯一实例的主键与计划时刻。</summary>
    private async Task<(int Id, DateTime PlannedAt)> MaterializeTodayAsync(TimeOnly start, TimeOnly end)
    {
        var now = DateTime.Now;
        using (var context = _db.CreateContext())
        {
            var subject = new Subject { Name = "数学", Category = SubjectCategory.Study };
            var phase = new Phase
            {
                Name = "测试阶段",
                StartDate = DateOnly.FromDateTime(now),
                IsActive = true,
            };
            context.Subjects.Add(subject);
            context.Phases.Add(phase);
            context.SaveChanges();

            context.ScheduleEntries.Add(new ScheduleEntry
            {
                PhaseId = phase.Id,
                SubjectId = subject.Id,
                DayOfWeek = now.DayOfWeek,
                StartTime = start,
                EndTime = end,
                Enabled = true,
            });
            context.SaveChanges();
        }

        (await _schedule.MaterializeAsync(now)).Should().Be(1, "7 天视野内同一个周几只落一次");

        using var readContext = _db.CreateContext();
        var occurrence = readContext.Occurrences.Single();
        return (occurrence.Id, occurrence.PlannedAt);
    }

    /// <summary>种两条「今天生效、今天有课」的课表（同一科目与阶段），展开后返回两实例的主键。</summary>
    private async Task<(int FirstId, int SecondId)> MaterializeTwoCoursesAsync(
        TimeOnly firstStart, TimeOnly firstEnd, TimeOnly secondStart, TimeOnly secondEnd)
    {
        var now = DateTime.Now;
        using (var context = _db.CreateContext())
        {
            var subject = new Subject { Name = "数学", Category = SubjectCategory.Study };
            var phase = new Phase
            {
                Name = "测试阶段",
                StartDate = DateOnly.FromDateTime(now),
                IsActive = true,
            };
            context.Subjects.Add(subject);
            context.Phases.Add(phase);
            context.SaveChanges();

            context.ScheduleEntries.Add(new ScheduleEntry
            {
                PhaseId = phase.Id,
                SubjectId = subject.Id,
                DayOfWeek = now.DayOfWeek,
                StartTime = firstStart,
                EndTime = firstEnd,
                Enabled = true,
            });
            context.ScheduleEntries.Add(new ScheduleEntry
            {
                PhaseId = phase.Id,
                SubjectId = subject.Id,
                DayOfWeek = now.DayOfWeek,
                StartTime = secondStart,
                EndTime = secondEnd,
                Enabled = true,
            });
            context.SaveChanges();
        }

        (await _schedule.MaterializeAsync(now)).Should().Be(2, "7 天视野内同一周几的两条课各落一次");

        using var readContext = _db.CreateContext();
        var first = readContext.Occurrences.Single(o => o.PlannedAt.Hour == firstStart.Hour);
        var second = readContext.Occurrences.Single(o => o.PlannedAt.Hour == secondStart.Hour);
        return (first.Id, second.Id);
    }

    /// <summary>读取某实例的当前状态（新开上下文，避免脏跟踪）。</summary>
    private OccurrenceStatus ReadStatus(int id)
    {
        using var context = _db.CreateContext();
        return context.Occurrences.Single(o => o.Id == id).Status;
    }

    /// <summary>读取唯一的完成记录。</summary>
    private CompletionLog ReadSingleCompletionLog()
    {
        using var context = _db.CreateContext();
        return context.CompletionLogs.Single();
    }
}
