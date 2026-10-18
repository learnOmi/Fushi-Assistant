using FluentAssertions;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Tests;

/// <summary>
/// ScheduleService 的集成测试：在真实的临时 SQLite 库上跑「展开 → 落库 → 上屏 → 触发」这条数据链。
/// 覆盖三件事：展开落库的幂等性、上屏时间线的组装、到期扫描与状态写入的闭环。
/// </summary>
public sealed class ScheduleServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<FuShiDbContext> _options;
    private readonly IDbContextFactory<FuShiDbContext> _factory;
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public ScheduleServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"fushi-test-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<FuShiDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        _factory = new TestContextFactory(_options);

        using var context = new FuShiDbContext(_options);
        context.Database.Migrate();
    }

    /// <summary>清理临时库（含 WAL/SHM 附属文件），并清空连接池以免残留连接锁文件。</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task MaterializeAsync_ShouldCreateOccurrencesForActivePhaseAndBeIdempotent()
    {
        var now = DateTime.Now;
        SeedSchedule(now, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var service = CreateService();

        var first = await service.MaterializeAsync(now);
        var second = await service.MaterializeAsync(now);

        first.Should().Be(1, "7 天视野内同一个「周几」只出现一次");
        second.Should().Be(0, "第二次展开应被去重算法与唯一索引挡住");
    }

    [Fact]
    public async Task MaterializeAsync_ShouldReturnZeroWhenNoActivePhase()
    {
        var service = CreateService();

        var created = await service.MaterializeAsync(DateTime.Now);

        created.Should().Be(0);
    }

    [Fact]
    public async Task GetTimelineAsync_ShouldReturnSlotsWithSubjectContext()
    {
        var now = DateTime.Now;
        SeedSchedule(now, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var service = CreateService();

        var today = DateOnly.FromDateTime(now);
        var slots = await service.GetTimelineAsync(today, today);

        slots.Should().ContainSingle();
        slots[0].SubjectName.Should().Be("数学");
        slots[0].SubjectColor.Should().Be(ModelDefaults.DEFAULT_SUBJECT_COLOR);
        slots[0].StartsAt.Date.Should().Be(now.Date);
    }

    [Fact]
    public async Task GetDueAsync_ShouldReturnDueOccurrence_AndMarkFiredRemovesIt()
    {
        var now = DateTime.Now;
        // 计划时刻取当天 00:00（必然早于 now，且晚于 now-1 天），使其落在扫描窗口 [now-1 天, now] 内。
        // 窗口取 1 天而非 1 小时，是为了让用例与「测试运行时刻」无关：无论几点跑，今天 00:00 都在窗口内。
        SeedSchedule(now, TimeOnly.MinValue, new TimeOnly(0, 1));
        var service = CreateService();

        var created = await service.MaterializeAsync(now);
        created.Should().Be(1);

        var due = await service.GetDueAsync(now.AddDays(-1), now);
        due.Should().ContainSingle();
        due[0].SubjectName.Should().Be("数学");

        await service.MarkFiredAsync(due[0].Occurrence.Id, now);

        var afterFire = await service.GetDueAsync(now.AddDays(-1), now);
        afterFire.Should().BeEmpty("已触发的实例不再参与扫描");

        var upcoming = await service.GetUpcomingAsync(now);
        upcoming.Should().BeEmpty("已触发的实例不再是未来待触发项");
    }

    /// <summary>建一个「今天生效、今天有课」的最小课表：阶段 + 科目 + 条目。</summary>
    private void SeedSchedule(DateTime now, TimeOnly start, TimeOnly end)
    {
        using var context = new FuShiDbContext(_options);

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

    private ScheduleService CreateService() =>
        new(_factory, new AppSettingRepository(_factory), _logger);

    /// <summary>测试用上下文工厂：把固定连接选项包成 IDbContextFactory。</summary>
    private sealed class TestContextFactory : IDbContextFactory<FuShiDbContext>
    {
        private readonly DbContextOptions<FuShiDbContext> _contextOptions;

        public TestContextFactory(DbContextOptions<FuShiDbContext> contextOptions) => _contextOptions = contextOptions;

        public FuShiDbContext CreateDbContext() => new(_contextOptions);
    }
}
