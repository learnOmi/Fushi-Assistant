using FluentAssertions;
using FuShi.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace FuShi.Tests;

/// <summary>
/// M5a 备份服务测试：快照可打开、同日不覆盖、保留策略只清自动备份、失败不抛异常。
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly string _backupDirectory =
        Path.Combine(Path.GetTempPath(), $"fushi-backup-test-{Guid.NewGuid():N}");

    private BackupService CreateService() => new(_database.Factory, _database.Logger, _backupDirectory);

    /// <summary>每日备份应生成一个可独立打开的完整 SQLite 库。</summary>
    [Fact]
    public async Task CreateDaily_WritesReadableDatabaseFile()
    {
        var result = await CreateService().CreateDailyIfNeededAsync(new DateTime(2026, 10, 22, 9, 30, 0));

        result.Success.Should().BeTrue(result.Error);
        result.FilePath.Should().NotBeNull();
        File.Exists(result.FilePath!).Should().BeTrue();
        Path.GetFileName(result.FilePath!).Should().Be("fushi-20261022.db");

        using var connection = new SqliteConnection($"Data Source={result.FilePath!};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
        Convert.ToInt32(command.ExecuteScalar()).Should().BeGreaterThan(0);
    }

    /// <summary>同一天第二次调用应跳过，不覆盖已存在的备份（文件内容保持原样）。</summary>
    [Fact]
    public async Task CreateDaily_WhenFileExists_SkipsWithoutOverwriting()
    {
        Directory.CreateDirectory(_backupDirectory);
        var existing = Path.Combine(_backupDirectory, "fushi-20261022.db");
        await File.WriteAllTextAsync(existing, "sentinel");

        var result = await CreateService().CreateDailyIfNeededAsync(new DateTime(2026, 10, 22, 18, 0, 0));

        result.Success.Should().BeTrue();
        result.FilePath.Should().Be(existing);
        (await File.ReadAllTextAsync(existing)).Should().Be("sentinel");
    }

    /// <summary>保留策略只清理自动备份，按日期保留最近 7 份；手动备份一份都不能删。</summary>
    [Fact]
    public void PruneAutoBackups_KeepsNewestSevenAutomatic_AndAllManual()
    {
        Directory.CreateDirectory(_backupDirectory);
        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(Path.Combine(_backupDirectory, $"fushi-202601{day:D2}.db"), "auto");
        }

        File.WriteAllText(Path.Combine(_backupDirectory, "fushi-manual-20260101_120000.db"), "manual-1");
        File.WriteAllText(Path.Combine(_backupDirectory, "fushi-manual-20260102_120000.db"), "manual-2");

        CreateService().PruneAutoBackups().Should().Be(2);

        var remainingAuto = Directory.GetFiles(_backupDirectory, "fushi-*.db")
            .Select(Path.GetFileName)
            .Where(static name => !name!.StartsWith("fushi-manual-", StringComparison.Ordinal))
            .ToList();

        remainingAuto.Should().HaveCount(BackupConstants.AUTO_RETAIN_COUNT);
        remainingAuto.Should().Contain("fushi-20260109.db");
        remainingAuto.Should().NotContain("fushi-20260101.db");
        File.Exists(Path.Combine(_backupDirectory, "fushi-manual-20260101_120000.db")).Should().BeTrue();
        File.Exists(Path.Combine(_backupDirectory, "fushi-manual-20260102_120000.db")).Should().BeTrue();
    }

    /// <summary>目标目录不可写时应返回失败结果，而不是把异常抛给启动路径。</summary>
    [Fact]
    public async Task CreateManual_WhenDirectoryUnavailable_ReturnsFailure()
    {
        // 用一个「文件」占据备份目录路径，CreateDirectory 必然失败。
        Directory.CreateDirectory(Path.GetDirectoryName(_backupDirectory)!);
        var blocker = _backupDirectory;
        await File.WriteAllTextAsync(blocker, "not a directory");

        var service = new BackupService(_database.Factory, _database.Logger, blocker);
        var result = await service.CreateManualAsync(new DateTime(2026, 10, 22, 10, 0, 0));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _database.Dispose();

        if (Directory.Exists(_backupDirectory))
        {
            Directory.Delete(_backupDirectory, recursive: true);
        }
        else if (File.Exists(_backupDirectory))
        {
            File.Delete(_backupDirectory);
        }
    }
}
