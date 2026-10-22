using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FuShi.Tests;

/// <summary>
/// 阶段管理的集成测试（M4a）：唯一生效、切换后的全量对账、删除保护。
/// 「切换」不是 UI 私事：库里的 IsActive 必须唯一，账本必须整体换成新阶段，
/// 且历史完成记录不能被切换或删除改写——这三条在这里钉死。
/// </summary>
public sealed class PhaseServiceTests
{
    /// <summary>测试用「当前时刻」：当天 12:00，保证 14:00 之类的时段落在未来。</summary>
    private static readonly DateTime Noon = DateTime.Today.AddHours(12);

    [Fact]
    public async Task CreateAsync_ShouldNotActivateNewPhase()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var edit = new ScheduleEditService(db.Factory, db.Logger);
        await edit.EnsureActivePhaseAsync();

        var created = await phaseService.CreateAsync("  实习期  ");

        created.Name.Should().Be("实习期", "名称应去除首尾空白");
        created.IsActive.Should().BeFalse("新建阶段不自动生效，切换只有一个入口");
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectBlankName()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);

        var act = async () => await phaseService.CreateAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ActivateAsync_ShouldKeepExactlyOneActivePhase()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var first = await phaseService.CreateAsync("默认课表");
        await phaseService.ActivateAsync(first.Id);
        var second = await phaseService.CreateAsync("实习期");

        var activated = await phaseService.ActivateAsync(second.Id);

        activated.Should().BeTrue();
        await using var context = db.CreateContext();
        var active = await context.Phases.Where(p => p.IsActive).ToListAsync();
        active.Should().ContainSingle().Which.Id.Should().Be(second.Id, "唯一生效：旧阶段必须被同一事务关掉");
    }

    [Fact]
    public async Task ActivateAsync_ShouldReturnFalseWhenMissing()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);

        var activated = await phaseService.ActivateAsync(999);

        activated.Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_ShouldTrimAndPersist()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var phase = await phaseService.CreateAsync("旧名");

        var renamed = await phaseService.RenameAsync(phase.Id, "  新名  ");

        renamed.Should().BeTrue();
        await using var context = db.CreateContext();
        (await context.Phases.SingleAsync()).Name.Should().Be("新名");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRejectActivePhase()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var phase = await phaseService.CreateAsync("默认课表");
        await phaseService.ActivateAsync(phase.Id);

        var outcome = await phaseService.DeleteAsync(phase.Id);

        outcome.Should().Be(PhaseDeleteOutcome.Active);
        await using var context = db.CreateContext();
        context.Phases.Should().ContainSingle("生效阶段不能被删空");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRejectPhaseWithHistory()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var edit = new ScheduleEditService(db.Factory, db.Logger);
        var active = await edit.EnsureActivePhaseAsync();
        var spare = await phaseService.CreateAsync("旧学期");
        var subject = await edit.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        var entry = await edit.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = spare.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Enabled = true,
        });

        await using (var context = db.CreateContext())
        {
            context.CompletionLogs.Add(new CompletionLog
            {
                Date = DateOnly.FromDateTime(DateTime.Today),
                EntryId = entry.Id,
                PlannedMin = 60,
                ActualMin = 60,
                Status = CompletionStatus.Done,
            });
            await context.SaveChangesAsync();
        }

        var outcome = await phaseService.DeleteAsync(spare.Id);

        outcome.Should().Be(PhaseDeleteOutcome.HasHistory);
        await using var verify = db.CreateContext();
        verify.Phases.Should().Contain(p => p.Id == spare.Id, "有历史记录的阶段不能硬删");
        active.Id.Should().NotBe(spare.Id);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCascadeEntriesOfUnusedPhase()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var edit = new ScheduleEditService(db.Factory, db.Logger);
        await edit.EnsureActivePhaseAsync();
        var spare = await phaseService.CreateAsync("旧学期");
        var subject = await edit.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        await edit.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = spare.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Enabled = true,
        });

        var outcome = await phaseService.DeleteAsync(spare.Id);

        outcome.Should().Be(PhaseDeleteOutcome.Deleted);
        await using var context = db.CreateContext();
        context.Phases.Should().NotContain(p => p.Id == spare.Id);
        context.ScheduleEntries.Should().BeEmpty("阶段删除会级联删除其课表条目");
    }

    [Fact]
    public async Task ReconcileAllAsync_ShouldDropOldPhaseAndExpandNewPhase()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var edit = new ScheduleEditService(db.Factory, db.Logger);
        var schedule = new ScheduleService(db.Factory, new AppSettingRepository(db.Factory), db.Logger);
        var subject = await edit.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);

        // 阶段 A（当前生效）：今天 14:00 有课，先展开出未来实例。
        var phaseA = await edit.EnsureActivePhaseAsync();
        var entryA = await EditEntryAsync(edit, phaseA.Id, subject.Id, new TimeOnly(14, 0));
        await schedule.MaterializeAsync(Noon);
        (await PendingForAsync(db, entryA.Id)).Should().NotBeEmpty();

        // 阶段 B：今天 16:00 有课；切换生效后全量对账。
        var phaseB = await phaseService.CreateAsync("实习期");
        var entryB = await EditEntryAsync(edit, phaseB.Id, subject.Id, new TimeOnly(16, 0));
        await phaseService.ActivateAsync(phaseB.Id);
        await schedule.ReconcileAllAsync(Noon);

        (await PendingForAsync(db, entryA.Id)).Should().BeEmpty("旧阶段的未来实例必须整片清除");
        (await PendingForAsync(db, entryB.Id)).Should().ContainSingle()
            .Which.PlannedAt.Hour.Should().Be(16, "新阶段按自己的课表重新展开");
    }

    [Fact]
    public async Task ReconcileAllAsync_ShouldKeepHistoricalOccurrences()
    {
        using var db = new TestDatabase();
        var phaseService = new PhaseService(db.Factory, db.Logger);
        var edit = new ScheduleEditService(db.Factory, db.Logger);
        var schedule = new ScheduleService(db.Factory, new AppSettingRepository(db.Factory), db.Logger);
        var subject = await edit.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        var phaseA = await edit.EnsureActivePhaseAsync();
        var entry = await EditEntryAsync(edit, phaseA.Id, subject.Id, new TimeOnly(9, 0));

        await using (var context = db.CreateContext())
        {
            // 一条「已触发」的历史与一条「已完成」的历史：切换阶段不得触碰。
            context.Occurrences.Add(new Occurrence
            {
                SourceType = OwnerType.ScheduleEntry,
                SourceId = entry.Id,
                PlannedAt = Noon.AddDays(-1).AddHours(-3),
                FiredAt = Noon.AddDays(-1).AddHours(-3),
                Status = OccurrenceStatus.Done,
            });
            await context.SaveChangesAsync();
        }

        var phaseB = await phaseService.CreateAsync("实习期");
        await phaseService.ActivateAsync(phaseB.Id);
        await schedule.ReconcileAllAsync(Noon);

        await using var verify = db.CreateContext();
        var history = await verify.Occurrences.SingleAsync();
        history.Status.Should().Be(OccurrenceStatus.Done, "历史账本不随阶段切换改写");
        history.SourceId.Should().Be(entry.Id);
    }

    /// <summary>在指定阶段下建一条「测试日有课」的模板。</summary>
    private static Task<ScheduleEntry> EditEntryAsync(
        ScheduleEditService edit,
        int phaseId,
        int subjectId,
        TimeOnly start) =>
        edit.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = phaseId,
            SubjectId = subjectId,
            DayOfWeek = Noon.DayOfWeek,
            StartTime = start,
            EndTime = start.Add(TimeSpan.FromHours(1)),
            Enabled = true,
        });

    /// <summary>取某来源下所有 Pending 实例。</summary>
    private static async Task<List<Occurrence>> PendingForAsync(TestDatabase db, int entryId)
    {
        await using var context = db.CreateContext();
        return await context.Occurrences
            .Where(o => o.SourceType == OwnerType.ScheduleEntry
                        && o.SourceId == entryId
                        && o.Status == OccurrenceStatus.Pending)
            .ToListAsync();
    }
}
