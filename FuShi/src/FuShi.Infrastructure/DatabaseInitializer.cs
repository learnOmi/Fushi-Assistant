using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure;

/// <summary>
/// 数据库初始化：启动时确保库存在并升级到最新迁移（设计文档 4.2）。
/// 数据库损坏或缺失时自动重建（NFR-6 自恢复）；重建也失败则向上抛出，绝不静默吞掉。
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>迁移失败时尝试重建数据库。</summary>
    /// <param name="factory">上下文工厂（短生命周期实例的来源）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public static void MigrateOrRebuild(IDbContextFactory<FuShiDbContext> factory, ILogger logger)
    {
        StoragePaths.EnsureAppDataDirectory();

        try
        {
            using var context = factory.CreateDbContext();
            context.Database.Migrate();
            logger.Information("数据库就绪：{Path}", StoragePaths.DatabasePath);
        }
        catch (Exception ex)
        {
            // 迁移失败：多为库损坏或结构不兼容。记录原因后走重建路径。
            logger.Error(ex, "数据库迁移失败，尝试重建：{Path}", StoragePaths.DatabasePath);
            Rebuild(factory, logger);
        }
    }

    /// <summary>删除库文件（含 WAL/SHM 附属文件）后重新迁移建库。</summary>
    /// <param name="factory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    private static void Rebuild(IDbContextFactory<FuShiDbContext> factory, ILogger logger)
    {
        // 先清空 SQLite 连接池，否则仍被持有的连接会锁住文件导致删除失败。
        SqliteConnection.ClearAllPools();

        foreach (var suffix in DATABASE_SIDE_FILE_SUFFIXES)
        {
            var path = StoragePaths.DatabasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        using var context = factory.CreateDbContext();
        context.Database.Migrate();
        logger.Information("数据库已重建：{Path}", StoragePaths.DatabasePath);
    }

    /// <summary>SQLite 主文件之外可能出现的附属文件后缀（预写日志 + 共享内存）。</summary>
    private static readonly string[] DATABASE_SIDE_FILE_SUFFIXES = { string.Empty, "-wal", "-shm" };
}
