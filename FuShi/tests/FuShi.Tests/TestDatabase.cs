using FuShi.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Tests;

/// <summary>
/// 测试用的临时 SQLite 库：建在临时目录、跑完清理（含 WAL/SHM）。
/// 封装「选项 + 上下文工厂 + 日志器」，供 ScheduleEditService / ScheduleService 的集成测试复用。
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _databasePath;

    /// <summary>固定连接选项。</summary>
    public DbContextOptions<FuShiDbContext> Options { get; }

    /// <summary>上下文工厂（每次创建短生命周期上下文）。</summary>
    public IDbContextFactory<FuShiDbContext> Factory { get; }

    /// <summary>测试用 Serilog 日志器（静默，不落盘）。</summary>
    public ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();

    /// <summary>建临时库并升级到最新迁移。</summary>
    public TestDatabase()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"fushi-test-{Guid.NewGuid():N}.db");
        Options = new DbContextOptionsBuilder<FuShiDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        Factory = new TestContextFactory(Options);

        using var context = new FuShiDbContext(Options);
        context.Database.Migrate();
    }

    /// <summary>开一个用于断言/种数据的直接上下文（用完即弃）。</summary>
    public FuShiDbContext CreateContext() => new(Options);

    /// <summary>清理临时库文件并清空连接池（避免残留连接锁文件）。</summary>
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

    /// <summary>把固定连接选项包成 IDbContextFactory。</summary>
    private sealed class TestContextFactory : IDbContextFactory<FuShiDbContext>
    {
        private readonly DbContextOptions<FuShiDbContext> _contextOptions;

        public TestContextFactory(DbContextOptions<FuShiDbContext> contextOptions) => _contextOptions = contextOptions;

        public FuShiDbContext CreateDbContext() => new(_contextOptions);
    }
}
