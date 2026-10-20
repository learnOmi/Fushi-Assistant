using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FuShi.Tests;

/// <summary>
/// 课表对账（ReconcileAsync）的集成测试：M3 技术核心。
/// 验证口径「改模板只影响未来，已发生的账本不改写」——
/// 改时间后旧未来实例被清除并重展开；删条目后不留幽灵；过去/已触发的账本不动。
/// </summary>
public sealed class ScheduleReconcileTests
{
    /// <summary>对账用的「当前时刻」：取当天 12:00，使 14:00 之类的写入必然落在未来。</summary>
    private static readonly DateTime Noon = DateTime.Today.AddHours(12);

    [Fact]
    public async Task ReconcileAsync_ShouldRemoveOldFutureAndRecreateNew()
    {
        using var db = new TestDatabase();
        var (edit, schedule) = CreateServices(db);
        var entry = await SeedEntryAsync(db, edit, new TimeOnly(14, 0), new TimeOnly(15, 0));

        await schedule.MaterializeAsync(Noon);
        var before = await PendingForAsync(db, entry.Id);
        before.Should().ContainSingle().Which.PlannedAt.Hour.Should().Be(14);

        // 把模板改到 16:00 → 对账。
        entry.StartTime = new TimeOnly(16, 0);
        entry.EndTime = new TimeOnly(17, 0);
        await edit.UpsertEntryAsync(entry);
        await schedule.ReconcileAsync(new[] { entry.Id }, Noon);

        var after = await PendingForAsync(db, entry.Id);
        after.Should().ContainSingle();
        after[0].PlannedAt.Hour.Should().Be(16, "旧 14:00 已清除、按新模板重展开");
        after.Should().NotContain(o => o.PlannedAt.Hour == 14, "旧时刻不再触发（无幽灵提醒）");
    }

    [Fact]
    public async Task ReconcileAsync_ShouldLeaveNoGhostAfterEntryDeleted()
    {
        using var db = new TestDatabase();
        var (edit, schedule) = CreateServices(db);
        var entry = await SeedEntryAsync(db, edit, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await schedule.MaterializeAsync(Noon);

        await edit.DeleteEntryAsync(entry.Id);
        await schedule.ReconcileAsync(new[] { entry.Id }, Noon);

        var after = await PendingForAsync(db, entry.Id);
        after.Should().BeEmpty("删条目后其未来待触发实例被一并清除");
    }

    [Fact]
    public async Task ReconcileAsync_ShouldNotTouchPastOrFiredLedger()
    {
        using var db = new TestDatabase();
        var (edit, schedule) = CreateServices(db);

        // 模板排在 00:00（相对 Noon 属「过去」）；展开后该实例即落在过去、状态仍为 Pending。
        var entry = await SeedEntryAsync(db, edit, new TimeOnly(0, 0), new TimeOnly(0, 30));
        await schedule.MaterializeAsync(Noon);

        // 改到 20:00 再对账。
        entry.StartTime = new TimeOnly(20, 0);
        entry.EndTime = new TimeOnly(21, 0);
        await edit.UpsertEntryAsync(entry);
        await schedule.ReconcileAsync(new[] { entry.Id }, Noon);

        var rows = await PendingForAsync(db, entry.Id);
        rows.Should().Contain(o => o.PlannedAt.Hour == 0, "过去时刻的账本不回写");
        rows.Should().Contain(o => o.PlannedAt.Hour == 20, "新时刻正常重建");
    }

    [Fact]
    public async Task ReconcileAsync_ShouldBeIdempotent()
    {
        using var db = new TestDatabase();
        var (edit, schedule) = CreateServices(db);
        var entry = await SeedEntryAsync(db, edit, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await schedule.MaterializeAsync(Noon);

        await schedule.ReconcileAsync(new[] { entry.Id }, Noon);
        await schedule.ReconcileAsync(new[] { entry.Id }, Noon);

        var rows = await PendingForAsync(db, entry.Id);
        rows.Should().ContainSingle("重复对账不应产生重复实例");
    }

    [Fact]
    public async Task ReconcileAsync_ShouldRollBackSameBatchShiftedNeighbours()
    {
        using var db = new TestDatabase();
        var (edit, schedule) = CreateServices(db);
        var first = await SeedEntryAsync(db, edit, new TimeOnly(14, 0), new TimeOnly(15, 0));
        var second = await SeedEntryAsync(db, edit, new TimeOnly(15, 0), new TimeOnly(16, 0));
        await schedule.MaterializeAsync(Noon);

        // 模拟一次「稍后」：被点的第一节与其后继第二节被同一条链顺延 +10 分钟，共用批次号 1。
        await using (var context = db.CreateContext())
        {
            foreach (var occurrence in await context.Occurrences.ToListAsync())
            {
                occurrence.ShiftMinutes = TimeDefaults.DEFAULT_SNOOZE_MINUTES;
                occurrence.ShiftBatchId = 1;
            }

            await context.SaveChangesAsync();
        }

        // 改第一节模板到 16:00 后对账：第一节旧实例清除重建，第二节（同批次配角）应被一并回退。
        first.StartTime = new TimeOnly(16, 0);
        first.EndTime = new TimeOnly(17, 0);
        await edit.UpsertEntryAsync(first);
        await schedule.ReconcileAsync(new[] { first.Id }, Noon);

        await using (var context = db.CreateContext())
        {
            var neighbour = await context.Occurrences.SingleAsync(o => o.SourceId == second.Id);
            neighbour.ShiftMinutes.Should().Be(0, "同批次的配角应随对账回退顺延量");
            neighbour.ShiftBatchId.Should().BeNull("批次号回退后应撤销，不留残余错位");
            neighbour.PlannedAt.Hour.Should().Be(15, "配角时刻本身不变，只是不再顺延");
        }
    }

    /// <summary>构造写/读两个服务。</summary>
    private static (ScheduleEditService Edit, ScheduleService Schedule) CreateServices(TestDatabase db) =>
        (new ScheduleEditService(db.Factory, db.Logger),
         new ScheduleService(db.Factory, new AppSettingRepository(db.Factory), db.Logger));

    /// <summary>种一条「今天（Noon 所在周几）有课」的模板并返回它。</summary>
    private static async Task<ScheduleEntry> SeedEntryAsync(
        TestDatabase db,
        ScheduleEditService edit,
        TimeOnly start,
        TimeOnly end)
    {
        var phase = await edit.EnsureActivePhaseAsync();
        var subject = await edit.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        return await edit.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subject.Id,
            DayOfWeek = Noon.DayOfWeek,
            StartTime = start,
            EndTime = end,
            Enabled = true,
        });
    }

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
