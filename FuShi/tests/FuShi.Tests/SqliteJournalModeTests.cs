using FluentAssertions;
using FuShi.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FuShi.Tests;

/// <summary>
/// <see cref="SqliteJournalMode"/> 的单元测试：验证日志模式能被固化为 DELETE、
/// 以及固化之后的 EF 迁移不会再把它翻回 WAL（后者是启动不产生 -wal/-shm 的前提）。
/// </summary>
public class SqliteJournalModeTests : IDisposable
{
    /// <summary>每个用例独立的临时目录，避免彼此干扰。</summary>
    private readonly string _workDirectory;

    /// <summary>本用例的数据库路径（尚未创建）。</summary>
    private readonly string _databasePath;

    /// <summary>SQLite 附属文件后缀（预写日志 + 共享内存）。</summary>
    private static readonly string[] SIDE_FILE_SUFFIXES = { "-wal", "-shm" };

    public SqliteJournalModeTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"fushi-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        _databasePath = Path.Combine(_workDirectory, "fushi.db");
    }

    [Fact]
    public void IsDelete_ShouldBeCaseInsensitive()
    {
        SqliteJournalMode.IsDelete("delete").Should().BeTrue();
        SqliteJournalMode.IsDelete("DELETE").Should().BeTrue();
        SqliteJournalMode.IsDelete("wal").Should().BeFalse();
        SqliteJournalMode.IsDelete(null).Should().BeFalse();
    }

    [Fact]
    public void EnsureDelete_ShouldCreateEmptyDatabaseAndReturnDelete()
    {
        SqliteJournalMode.EnsureDelete(_databasePath).Should().Be("delete");

        File.Exists(_databasePath).Should().BeTrue("库不存在时应顺带建出一个空库，避免 EF 走建库路径又设成 WAL");
        ReadJournalMode(_databasePath).Should().Be("delete");
    }

    [Fact]
    public void EnsureDelete_ShouldConvertWalDatabaseToDelete()
    {
        // 先造一个 WAL 库（模拟历史遗留：EF Migrate() 建库时会给新库设 WAL）。
        using (var connection = Open(_databasePath))
        {
            Execute(connection, "PRAGMA journal_mode=WAL; CREATE TABLE t(x INTEGER); INSERT INTO t VALUES (1);");
        }
        SqliteConnection.ClearAllPools();
        ReadJournalMode(_databasePath).Should().Be("wal");

        SqliteJournalMode.EnsureDelete(_databasePath).Should().Be("delete");

        ReadJournalMode(_databasePath).Should().Be("delete");
        SideFiles(_databasePath).Should().BeEmpty("切换后不应再残留 -wal/-shm");
    }

    [Fact]
    public void EnsureDelete_ShouldBeIdempotent()
    {
        SqliteJournalMode.EnsureDelete(_databasePath).Should().Be("delete");
        SqliteJournalMode.EnsureDelete(_databasePath).Should().Be("delete");

        ReadJournalMode(_databasePath).Should().Be("delete");
    }

    [Fact]
    public void Migrate_AfterEnsureDelete_ShouldKeepDeleteModeEvenForFreshDatabase()
    {
        // 首次安装的关键路径：先由 EnsureDelete 建空库并固化 DELETE，再交给 EF 建表。
        SqliteJournalMode.EnsureDelete(_databasePath).Should().Be("delete");

        var options = new DbContextOptionsBuilder<FuShiDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        using (var context = new FuShiDbContext(options))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        ReadJournalMode(_databasePath).Should().Be("delete", "EF 只在自行建库时设 WAL，对既有库迁移不会改动日志模式");
        SideFiles(_databasePath).Should().BeEmpty("DELETE 模式下迁移不应产生 -wal/-shm");
        TableCount(_databasePath).Should().BeGreaterOrEqualTo(8, "8 张业务表应已全部建出");
    }

    /// <summary>打开库文件并读出当前日志模式（读不到文件时返回空串）。</summary>
    private static string ReadJournalMode(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return command.ExecuteScalar()?.ToString()?.ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>统计库内用户表数量。</summary>
    private static long TableCount(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>列出当前存在的附属文件后缀。</summary>
    private static string[] SideFiles(string path) =>
        SIDE_FILE_SUFFIXES.Where(suffix => File.Exists(path + suffix)).ToArray();

    /// <summary>打开（不存在则创建）数据库连接。</summary>
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    /// <summary>执行一段 SQL。</summary>
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
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

        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, true);
        }
    }
}
