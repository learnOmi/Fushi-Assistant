using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Data;

/// <summary>
/// 数据库备份服务（M5a）：每日自动备份 + 手动备份 + 自动备份保留策略。
/// <para>
/// 快照方式统一用 <c>VACUUM INTO</c>（见 <see cref="BackupConstants.VACUUM_INTO_FORMAT"/>），
/// 产物是单个一致性文件，用户可以直接拷走或改名恢复。
/// </para>
/// <para>
/// 所有失败都收敛为 <see cref="BackupResult"/> 而不抛异常：备份是「锦上添花」，
/// 目录被安全软件拦截、磁盘满等都不应阻断应用启动或把设置页点崩。
/// </para>
/// </summary>
public sealed class BackupService
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;
    private readonly string _backupDirectory;

    /// <summary>构造备份服务（使用默认备份目录 %APPDATA%\FuShi\backups）。</summary>
    /// <param name="contextFactory">上下文工厂（短生命周期上下文）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public BackupService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
        : this(contextFactory, logger, StoragePaths.BackupDirectory)
    {
    }

    /// <summary>构造备份服务并指定备份目录（测试用）。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="backupDirectory">备份目录绝对路径。</param>
    public BackupService(
        IDbContextFactory<FuShiDbContext> contextFactory,
        ILogger logger,
        string backupDirectory)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _backupDirectory = backupDirectory;
    }

    /// <summary>备份目录绝对路径（设置页展示用）。</summary>
    public string BackupDirectory => _backupDirectory;

    /// <summary>
    /// 每日自动备份：<c>fushi-YYYYMMDD.db</c> 已存在则跳过（保证一天一份、多次启动不重复），
    /// 随后按保留策略清理多余的自动备份（手动备份不参与）。
    /// </summary>
    /// <param name="now">当前时刻（决定文件名中的日期）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<BackupResult> CreateDailyIfNeededAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            _backupDirectory,
            string.Format(CultureInfo.InvariantCulture, BackupConstants.AUTO_FILE_FORMAT, now));

        if (File.Exists(path))
        {
            PruneAutoBackups();
            return BackupResult.Ok(path);
        }

        var result = await CreateSnapshotAsync(path, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            PruneAutoBackups();
        }

        return result;
    }

    /// <summary>手动备份：<c>fushi-manual-YYYYMMDD_HHmmss.db</c>，不参与自动清理。</summary>
    /// <param name="now">当前时刻（决定文件名中的时间戳）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<BackupResult> CreateManualAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var basePath = Path.Combine(
            _backupDirectory,
            string.Format(CultureInfo.InvariantCulture, BackupConstants.MANUAL_FILE_FORMAT, now));

        return CreateSnapshotAsync(FileNaming.EnsureUnique(basePath), cancellationToken);
    }

    /// <summary>
    /// 最近一次备份的时刻（自动 + 手动取最新），供设置页展示；没有备份时返回 null。
    /// 以文件写入时间为准，避免依赖文件名解析的边界格式。
    /// </summary>
    public DateTime? GetLastBackupTime()
    {
        try
        {
            if (!Directory.Exists(_backupDirectory))
            {
                return null;
            }

            var latest = Directory
                .GetFiles(_backupDirectory, BackupConstants.FILE_SEARCH_PATTERN)
                .Select(static path => new FileInfo(path))
                .OrderByDescending(static info => info.LastWriteTimeUtc)
                .FirstOrDefault();

            return latest?.LastWriteTime;
        }
        catch (Exception ex)
        {
            // 目录不可读只影响展示，记调试日志后返回 null。
            _logger.Debug(ex, "读取最近备份时间失败：{Directory}", _backupDirectory);
            return null;
        }
    }

    /// <summary>
    /// 清理自动备份：按文件名（日期）倒序保留最近 <see cref="BackupConstants.AUTO_RETAIN_COUNT"/> 份。
    /// 手动备份前缀不同，天然不在清理范围内。
    /// </summary>
    /// <returns>本次删除的文件数。</returns>
    public int PruneAutoBackups()
    {
        try
        {
            if (!Directory.Exists(_backupDirectory))
            {
                return 0;
            }

            var stale = ListAutoBackups()
                .Skip(BackupConstants.AUTO_RETAIN_COUNT)
                .ToList();

            foreach (var path in stale)
            {
                File.Delete(path);
                _logger.Information("清理过期自动备份：{Path}", path);
            }

            return stale.Count;
        }
        catch (Exception ex)
        {
            // 清理失败不影响本次备份结果，只记警告（下次启动会再试）。
            _logger.Warning(ex, "清理自动备份失败：{Directory}", _backupDirectory);
            return 0;
        }
    }

    /// <summary>列出自动备份文件（按文件名倒序，即最新在前）。</summary>
    public IReadOnlyList<string> ListAutoBackups() =>
        Directory.Exists(_backupDirectory)
            ? Directory
                .GetFiles(_backupDirectory, BackupConstants.FILE_SEARCH_PATTERN)
                .Where(IsAutoBackup)
                .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList()
            : Array.Empty<string>();

    /// <summary>文件名是否属于自动备份（带自动前缀但不带手动前缀）。</summary>
    /// <param name="path">文件绝对路径。</param>
    private static bool IsAutoBackup(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(BackupConstants.AUTO_FILE_PREFIX, StringComparison.Ordinal)
               && !name.StartsWith(BackupConstants.MANUAL_FILE_PREFIX, StringComparison.Ordinal);
    }

    /// <summary>
    /// 用 <c>VACUUM INTO</c> 生成一致性快照。目标文件必须不存在（SQLite 会拒绝覆盖）。
    /// 失败时尝试删除半成品并返回失败结果，绝不把异常抛给调用方。
    /// </summary>
    /// <param name="targetPath">目标文件绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<BackupResult> CreateSnapshotAsync(string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_backupDirectory);

            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            // 路径中的单引号按 SQL 字面量规则转义（Windows 路径极少出现，但转义成本为零）。
            var escaped = targetPath.Replace("'", "''", StringComparison.Ordinal);
            var sql = string.Format(CultureInfo.InvariantCulture, BackupConstants.VACUUM_INTO_FORMAT, escaped);

            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            _logger.Information("数据库备份完成：{Path}", targetPath);
            return BackupResult.Ok(targetPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "数据库备份失败：{Path}", targetPath);
            TryDeletePartial(targetPath);
            return BackupResult.Fail(ex.Message);
        }
    }

    /// <summary>删除失败时可能留下的半成品文件；删除失败无处可上报，忽略即可。</summary>
    /// <param name="path">目标文件路径。</param>
    private void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "清理备份半成品失败：{Path}", path);
        }
    }
}
