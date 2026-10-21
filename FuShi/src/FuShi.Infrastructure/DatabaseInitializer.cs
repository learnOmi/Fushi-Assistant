using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure;

/// <summary>
/// 数据库初始化：启动时确保库存在并升级到最新迁移（设计文档 4.2）。
/// 恢复策略为**分级、非破坏性**——顺序即优先级，能轻就不重：
/// <list type="number">
/// <item>先把日志模式固化为 DELETE，从根上杜绝需要内存映射的 <c>-wal</c>/<c>-shm</c>；</item>
/// <item>迁移失败先「清连接池 + 退避」重试若干次（瞬时 I/O 抖动多数下一次就能过）；</item>
/// <item>只有连续失败才判定为不可恢复：把旧库**改名备份**后重建，不直接删除用户数据；</item>
/// <item>重建仍失败则向上抛出（NFR-6 自恢复），绝不静默吞掉。</item>
/// </list>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>迁移尝试次数上限（含首次）。</summary>
    private const int MIGRATE_MAX_ATTEMPTS = 3;

    /// <summary>重试的基础退避毫秒数，按尝试序号线性放大（300 → 600 → 900）。</summary>
    private const int MIGRATE_RETRY_BASE_DELAY_MILLISECONDS = 300;

    /// <summary>损坏库备份名的中缀，拼出形如 <c>fushi.db.broken-20261020_213000</c> 的备份文件。</summary>
    private const string BROKEN_DATABASE_BACKUP_INFIX = ".broken-";

    /// <summary>备份名时间戳格式（本地时间，便于用户辨认是哪一次启动触发的）。</summary>
    private const string BACKUP_TIMESTAMP_FORMAT = "yyyyMMdd_HHmmss";

    /// <summary>
    /// 与主库同路径、只是后缀不同的全部文件（空串即主库本身）。
    /// 备份时整体改名，保证备份是一份**可独立打开**的完整库。
    /// <para>
    /// 必须包含 <c>-journal</c>：DELETE 模式下进程被强杀会留下回滚日志（热日志）。
    /// 若只搬走主库而把 <c>-journal</c> 落在原处，SQLite 打开**新建的空库**时会把这份
    /// 属于旧库的回滚日志回放进去，反而把新库写坏。
    /// </para>
    /// </summary>
    private static readonly string[] ALL_DATABASE_FILE_SUFFIXES = { string.Empty, "-wal", "-shm", "-journal" };

    /// <summary>
    /// 确保数据库可用：固化日志模式 → 迁移（带重试）→ 仍失败才备份重建。
    /// </summary>
    /// <param name="factory">上下文工厂（短生命周期实例的来源）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public static void MigrateOrRebuild(IDbContextFactory<FuShiDbContext> factory, ILogger logger)
    {
        StoragePaths.EnsureAppDataDirectory();

        // ① 固化 DELETE：库不存在时会顺带建出一个空库，使 EF 走「迁移既有库」而非「自行建库」，
        //    从而不会被 EF 顺手设成 WAL（实测 EF 建库会给新库设 WAL）。该 PRAGMA 还会把
        //    历史 -wal 里已提交的事务回放进主库并删掉附属文件，是消除残留的安全做法。
        var journalMode = SqliteJournalMode.EnsureDelete(StoragePaths.DatabasePath, logger);
        if (!SqliteJournalMode.IsDelete(journalMode))
        {
            logger.Warning("本次数据库日志模式为 {Mode}（非 DELETE），-wal/-shm 仍有出现的可能", journalMode);
        }

        // ② 迁移重试：瞬时 I/O 抖动（多为安全软件/过滤驱动拦截）通常下一次就能过。
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MIGRATE_MAX_ATTEMPTS; attempt++)
        {
            try
            {
                using var context = factory.CreateDbContext();
                context.Database.Migrate();
                logger.Information("数据库就绪：{Path}", StoragePaths.DatabasePath);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;

                // 失败那次的连接仍挂在池里（可能仍持有文件句柄），必须清掉再重试。
                // 注意：**不删 -wal**——那里面可能有已提交事务，重开时 SQLite 会自行回放。
                SqliteConnection.ClearAllPools();

                logger.Warning(ex, "数据库迁移第 {Attempt}/{Max} 次失败：{Path}",
                    attempt, MIGRATE_MAX_ATTEMPTS, StoragePaths.DatabasePath);

                if (attempt < MIGRATE_MAX_ATTEMPTS)
                {
                    Thread.Sleep(MIGRATE_RETRY_BASE_DELAY_MILLISECONDS * attempt);
                }
            }
        }

        // ③ 连续失败：判定为不可恢复，转重建（重建前先备份旧库，不再直接抹掉用户数据）。
        logger.Error(lastError, "数据库迁移连续 {Max} 次失败，判定不可恢复，备份后重建：{Path}",
            MIGRATE_MAX_ATTEMPTS, StoragePaths.DatabasePath);
        Rebuild(factory, logger);
    }

    /// <summary>
    /// 把旧库（含附属文件）整体改名备份，再新建空库并迁移。
    /// 重建仍失败则记 Fatal 并向上抛出，由启动流程决定如何提示，绝不静默。
    /// </summary>
    /// <param name="factory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    private static void Rebuild(IDbContextFactory<FuShiDbContext> factory, ILogger logger)
    {
        SqliteConnection.ClearAllPools();
        BackupBrokenDatabase(logger);

        // 备份把主库移走后，这里重新建一个空库并再次固化 DELETE，
        // 否则 EF 的建库路径会把新库又设成 WAL。
        SqliteJournalMode.EnsureDelete(StoragePaths.DatabasePath, logger);

        try
        {
            using var context = factory.CreateDbContext();
            context.Database.Migrate();
            logger.Information("数据库已重建：{Path}", StoragePaths.DatabasePath);
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "数据库重建失败，无法启动：{Path}", StoragePaths.DatabasePath);
            throw;
        }
    }

    /// <summary>
    /// 把损坏库改名成 <c>fushi.db.broken-&lt;时间戳&gt;</c>（主库与附属文件一起改，
    /// 保证备份可独立打开），保留现场供事后取证，而不是直接删除。
    /// </summary>
    /// <param name="logger">Serilog 日志器。</param>
    private static void BackupBrokenDatabase(ILogger logger)
    {
        var backupBase = StoragePaths.DatabasePath + BROKEN_DATABASE_BACKUP_INFIX
            + DateTime.Now.ToString(BACKUP_TIMESTAMP_FORMAT);

        foreach (var suffix in ALL_DATABASE_FILE_SUFFIXES)
        {
            var source = StoragePaths.DatabasePath + suffix;
            if (!File.Exists(source))
            {
                continue;
            }

            var target = backupBase + suffix;
            // 同目录内改名，不存在跨卷失败；也不覆盖同名文件（改名会抛，从而保留既有备份）。
            File.Move(source, target);
            logger.Information("损坏库已备份：{Source} → {Target}", source, target);
        }
    }
}
