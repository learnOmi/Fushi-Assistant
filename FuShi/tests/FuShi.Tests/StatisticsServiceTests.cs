using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure.Statistics;

namespace FuShi.Tests;

/// <summary>
/// 统计聚合的集成测试（M4b）：区间边界、跨科目分布、未分类兜底，
/// 以及「历史快照不被删条目/改科目追溯改写」这条统计口径的核心承诺。
/// 「最近 7 天」用显式传入的 today 界定窗口，不依赖跑测试时的真实时钟。
/// </summary>
public sealed class StatisticsServiceTests
{
    /// <summary>测试基准日（窗口计算全以它为准）。</summary>
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public async Task GetAsync_ShouldIncludeBothRangeBoundaries()
    {
        using var db = new TestDatabase();
        var service = new StatisticsService(db.Factory, db.Logger);

        await AddLogAsync(db, Today.AddDays(-1), 10, 10, CompletionStatus.Done);
        await AddLogAsync(db, Today, 20, 15, CompletionStatus.Done);
        await AddLogAsync(db, Today.AddDays(3), 30, 30, CompletionStatus.Done);
        await AddLogAsync(db, Today.AddDays(4), 40, 40, CompletionStatus.Done);

        var report = await service.GetAsync(Today, Today.AddDays(3), Today);

        report.TotalCount.Should().Be(2, "双闭区间：起止两天都要算进来，区间外的不算");
        report.PlannedMinutes.Should().Be(50);
        report.ActualMinutes.Should().Be(45);
        report.Subjects.Should().ContainSingle().Which.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldGroupBySubjectAndSharePlannedMinutes()
    {
        using var db = new TestDatabase();
        var mathId = await AddSubjectAsync(db, "数学", "#111111");
        var englishId = await AddSubjectAsync(db, "英语", "#222222");
        var service = new StatisticsService(db.Factory, db.Logger);

        await AddLogAsync(db, Today, 60, 50, CompletionStatus.Done, mathId);
        await AddLogAsync(db, Today, 60, 0, CompletionStatus.Skipped, mathId);
        await AddLogAsync(db, Today, 40, 40, CompletionStatus.Done, englishId);

        var report = await service.GetAsync(Today, Today, Today);

        report.DoneCount.Should().Be(2);
        report.SkippedCount.Should().Be(1);
        report.MissedCount.Should().Be(0);
        report.CompletionRate.Should().BeApproximately(200.0 / 3, 0.001, "完成率 = 完成 2 / 记录 3");

        report.Subjects.Should().HaveCount(2);
        report.Subjects[0].SubjectName.Should().Be("数学", "按计划时长降序，数学 120 分钟排前");
        report.Subjects[0].PlannedMinutes.Should().Be(120);
        report.Subjects[0].DoneCount.Should().Be(1);
        report.Subjects[0].TotalCount.Should().Be(2);
        report.Subjects[0].SharePercent.Should().BeApproximately(75.0, 0.001, "120 / 160 = 75%");

        report.Subjects[1].SubjectName.Should().Be("英语");
        report.Subjects[1].SharePercent.Should().BeApproximately(25.0, 0.001);
    }

    [Fact]
    public async Task GetAsync_ShouldPutMissingSubjectIntoUncategorized()
    {
        using var db = new TestDatabase();
        var service = new StatisticsService(db.Factory, db.Logger);

        // SubjectId 为 NULL（旧数据回填失败）与「科目已被删除」两种都归「未分类」。
        await AddLogAsync(db, Today, 60, 60, CompletionStatus.Done);

        var deletedId = await AddSubjectAsync(db, "已删除科目", "#333333");
        await AddLogAsync(db, Today, 30, 0, CompletionStatus.Missed, deletedId);
        await using (var context = db.CreateContext())
        {
            context.Subjects.Remove(context.Subjects.Single(s => s.Id == deletedId));
            await context.SaveChangesAsync();
        }

        var report = await service.GetAsync(Today, Today, Today);

        report.Subjects.Should().ContainSingle("NULL 快照与已删除科目合并成同一个「未分类」桶，避免界面出现多行同名项");
        report.Subjects[0].SubjectId.Should().BeNull();
        report.Subjects[0].SubjectName.Should().Be("未分类");
        report.Subjects[0].PlannedMinutes.Should().Be(90);
        report.Subjects[0].TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldKeepSnapshotAfterEntryDeleted()
    {
        using var db = new TestDatabase();
        var subjectId = await AddSubjectAsync(db, "数学", "#111111");
        var entryId = await AddEntryAsync(db, subjectId);
        var service = new StatisticsService(db.Factory, db.Logger);

        await AddLogAsync(db, Today, 60, 45, CompletionStatus.Done, subjectId, entryId);

        await using (var context = db.CreateContext())
        {
            context.ScheduleEntries.Remove(context.ScheduleEntries.Single(e => e.Id == entryId));
            await context.SaveChangesAsync();
        }

        var report = await service.GetAsync(Today, Today, Today);

        report.TotalCount.Should().Be(1, "删条目不能删掉历史完成记录");
        report.Subjects.Should().ContainSingle()
            .Which.SubjectName.Should().Be("数学", "科目归属来自快照，不靠 EntryId 回查");
    }

    [Fact]
    public async Task GetAsync_ShouldNotRewriteHistoryWhenEntrySubjectChanges()
    {
        using var db = new TestDatabase();
        var mathId = await AddSubjectAsync(db, "数学", "#111111");
        var englishId = await AddSubjectAsync(db, "英语", "#222222");
        var entryId = await AddEntryAsync(db, mathId);
        var service = new StatisticsService(db.Factory, db.Logger);

        await AddLogAsync(db, Today, 60, 60, CompletionStatus.Done, mathId, entryId);

        // 把条目改成英语：历史那一条仍属于数学（快照口径）。
        await using (var context = db.CreateContext())
        {
            var entry = context.ScheduleEntries.Single(e => e.Id == entryId);
            entry.SubjectId = englishId;
            await context.SaveChangesAsync();
        }

        var report = await service.GetAsync(Today, Today, Today);

        report.Subjects.Should().ContainSingle()
            .Which.SubjectName.Should().Be("数学", "改科目只影响以后，不追溯改写历史统计");
    }

    [Fact]
    public async Task GetAsync_ShouldBuildRecentSevenDaysEndingToday()
    {
        using var db = new TestDatabase();
        var service = new StatisticsService(db.Factory, db.Logger);

        await AddLogAsync(db, Today.AddDays(-7), 60, 60, CompletionStatus.Done); // 窗口外
        await AddLogAsync(db, Today.AddDays(-6), 60, 0, CompletionStatus.Missed);
        await AddLogAsync(db, Today.AddDays(-1), 60, 60, CompletionStatus.Done);
        await AddLogAsync(db, Today, 60, 30, CompletionStatus.Done);
        await AddLogAsync(db, Today, 60, 0, CompletionStatus.Skipped);

        var report = await service.GetAsync(Today.AddDays(-10), Today, Today);

        report.RecentDays.Should().HaveCount(StatisticsService.RECENT_DAYS);
        report.RecentDays[0].Date.Should().Be(Today.AddDays(-6), "窗口是含今天在内的 7 天");
        report.RecentDays[^1].Date.Should().Be(Today);
        report.RecentDays.Should().NotContain(day => day.Date == Today.AddDays(-7), "第 8 天不进最近 7 天");

        report.RecentDays[0].CompletionRate.Should().Be(0, "只有一条错过 → 0%");
        report.RecentDays[0].HasData.Should().BeTrue();

        report.RecentDays[^1].CompletionRate.Should().BeApproximately(50.0, 0.001, "今天 1 完成 1 跳过");
        report.RecentDays[^2].CompletionRate.Should().BeApproximately(100.0, 0.001);

        report.RecentDays.Should().Contain(day => !day.HasData, "没有记录的日子也要保留一行，界面显示占位");
    }

    [Fact]
    public async Task GetAsync_ShouldRejectInvertedRange()
    {
        using var db = new TestDatabase();
        var service = new StatisticsService(db.Factory, db.Logger);

        var act = async () => await service.GetAsync(Today, Today.AddDays(-1), Today);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------- 测试辅助 ----------

    /// <summary>种一个科目，返回主键。</summary>
    private static async Task<int> AddSubjectAsync(TestDatabase db, string name, string color)
    {
        await using var context = db.CreateContext();
        var subject = new Subject { Name = name, Color = color, Category = SubjectCategory.Study };
        context.Subjects.Add(subject);
        await context.SaveChangesAsync();
        return subject.Id;
    }

    /// <summary>种一条最小时段模板（需要阶段与科目），返回条目主键。</summary>
    private static async Task<int> AddEntryAsync(TestDatabase db, int subjectId)
    {
        await using var context = db.CreateContext();
        var phase = new Phase
        {
            Name = "测试阶段",
            StartDate = Today,
            IsActive = true,
        };
        context.Phases.Add(phase);
        await context.SaveChangesAsync();

        var entry = new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subjectId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Enabled = true,
        };
        context.ScheduleEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    /// <summary>种一条完成记录；<paramref name="entryId"/> 默认 0，仅用于满足非空列。</summary>
    private static async Task AddLogAsync(
        TestDatabase db,
        DateOnly date,
        int planned,
        int actual,
        CompletionStatus status,
        int? subjectId = null,
        int entryId = 0)
    {
        await using var context = db.CreateContext();
        context.CompletionLogs.Add(new CompletionLog
        {
            Date = date,
            EntryId = entryId,
            SubjectId = subjectId,
            PlannedMin = planned,
            ActualMin = actual,
            Status = status,
        });
        await context.SaveChangesAsync();
    }
}
