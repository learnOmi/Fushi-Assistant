using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure.Scheduling;

namespace FuShi.Tests;

/// <summary>
/// 课表写服务的集成测试：阶段自举、科目 CRUD（含被引用时拒绝删除）、条目增改删。
/// </summary>
public sealed class ScheduleEditServiceTests
{
    [Fact]
    public async Task EnsureActivePhaseAsync_ShouldBootstrapWhenNoneExists()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);

        var phase = await service.EnsureActivePhaseAsync();

        phase.Id.Should().BeGreaterThan(0);
        phase.IsActive.Should().BeTrue();
        phase.Name.Should().Be(ModelDefaults.DEFAULT_PHASE_NAME);
        phase.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));

        var again = await service.EnsureActivePhaseAsync();
        again.Id.Should().Be(phase.Id, "已有生效阶段时不重复创建");
    }

    [Fact]
    public async Task EnsureActivePhaseAsync_ShouldReuseExistingActivePhase()
    {
        using var db = new TestDatabase();
        using (var context = db.CreateContext())
        {
            context.Phases.Add(new Phase { Name = "秋季学期", StartDate = new DateOnly(2026, 9, 1), IsActive = true });
            context.SaveChanges();
        }

        var service = new ScheduleEditService(db.Factory, db.Logger);
        var phase = await service.EnsureActivePhaseAsync();

        phase.Name.Should().Be("秋季学期");
    }

    [Fact]
    public async Task CreateSubjectAsync_ShouldRejectBlankName()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);

        var act = async () => await service.CreateSubjectAsync("   ", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertEntryAsync_ShouldInsertThenUpdate()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);
        var phase = await service.EnsureActivePhaseAsync();
        var subject = await service.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);

        var entry = new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Enabled = true,
        };

        var saved = await service.UpsertEntryAsync(entry);
        saved.Id.Should().BeGreaterThan(0);

        saved.StartTime = new TimeOnly(9, 30);
        await service.UpsertEntryAsync(saved);

        using var context = db.CreateContext();
        var row = context.ScheduleEntries.Single();
        row.StartTime.Should().Be(new TimeOnly(9, 30), "更新走的是同一行");
    }

    [Fact]
    public async Task DeleteEntryAsync_ShouldRemoveRow()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);
        var phase = await service.EnsureActivePhaseAsync();
        var subject = await service.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        var entry = await service.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
        });

        var deleted = await service.DeleteEntryAsync(entry.Id);

        deleted.Should().BeTrue();
        using var context = db.CreateContext();
        context.ScheduleEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSubjectAsync_ShouldReturnFalseWhenReferenced()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);
        var phase = await service.EnsureActivePhaseAsync();
        var subject = await service.CreateSubjectAsync("数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);
        await service.UpsertEntryAsync(new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
        });

        var deleted = await service.DeleteSubjectAsync(subject.Id);

        deleted.Should().BeFalse("科目被条目引用时外键 Restrict 会拒绝删除");
    }

    [Fact]
    public async Task UpdateSubjectAsync_ShouldReturnFalseWhenMissing()
    {
        using var db = new TestDatabase();
        var service = new ScheduleEditService(db.Factory, db.Logger);

        var updated = await service.UpdateSubjectAsync(999, "数学", ModelDefaults.DEFAULT_SUBJECT_COLOR, SubjectCategory.Study);

        updated.Should().BeFalse();
    }
}
