using Microsoft.Data.Sqlite;
using Serilog;

namespace FuShi.Infrastructure;

/// <summary>
/// 把 SQLite 库的日志模式固化为最保守的 DELETE（回滚日志与主库同目录、不需要内存映射）。
/// <para>
/// 背景：EF Core 的 <c>Migrate()</c> 在**新建库**时会把库置为 WAL，进而产生需要 mmap 的
/// <c>-wal</c>/<c>-shm</c> 附属文件；而这类文件容易被第三方安全软件的内核过滤驱动拦截，
/// 使迁移的第一条查询直接报 <c>SQLITE_IOERR(disk I/O error)</c>。
/// </para>
/// <para>
/// <c>journal_mode</c> 是**持久化在库文件头**里的属性：只要成功执行一次
/// <c>PRAGMA journal_mode=DELETE;</c> 即永久生效，之后由 EF 打开的连接也不会把它翻回 WAL
/// （实测：对存量库置 DELETE 后再跑 <c>Migrate()</c>，模式仍为 delete）。
/// 该 PRAGMA 还会顺带把 WAL 里已提交的事务**回放并删除** <c>-wal</c>/<c>-shm</c>，
/// 是消除历史残留的安全手段（直接删除 <c>-wal</c> 会丢已提交数据，绝不可取）。
/// </para>
/// </summary>
public static class SqliteJournalMode
{
    /// <summary>目标日志模式：DELETE（SQLite 默认值，不使用 WAL）。</summary>
    public const string JOURNAL_MODE_DELETE = "DELETE";

    /// <summary>连接字符串模板（只填路径，不附加任何 PRAGMA 参数）。</summary>
    private const string CONNECTION_STRING_TEMPLATE = "Data Source={0}";

    /// <summary>设置日志模式并读回实际生效值的 PRAGMA 语句。</summary>
    private const string PRAGMA_SET_DELETE = "PRAGMA journal_mode=DELETE;";

    /// <summary>
    /// 确保指定库的日志模式为 DELETE。
    /// 库文件不存在时会顺带创建一个**合法的空库**——这样 EF 后续走的是「迁移既有库」而非
    /// 「自行建库」，也就不会被 EF 顺手设成 WAL（实测 EF 建库会给新库设 WAL）。
    /// </summary>
    /// <param name="databasePath">数据库文件绝对路径（其父目录必须已存在）。</param>
    /// <param name="logger">Serilog 日志器；传 null 则静默。</param>
    /// <returns>读回的日志模式（正常为 <c>delete</c>，已归一为小写）；出错时为 null。</returns>
    public static string? EnsureDelete(string databasePath, ILogger? logger = null)
    {
        // 切换日志模式（尤其 WAL → DELETE）要求独占访问：先把连接池清空，
        // 避免池中闲置连接仍占着这个库、导致 PRAGMA 静默返回原模式而不是切换成功。
        SqliteConnection.ClearAllPools();

        try
        {
            using var connection = new SqliteConnection(string.Format(CONNECTION_STRING_TEMPLATE, databasePath));
            connection.Open();

            var applied = ExecuteScalar(connection, PRAGMA_SET_DELETE);
            if (IsDelete(applied))
            {
                logger?.Debug("SQLite 日志模式已固化为 DELETE：{Path}", databasePath);
            }
            else
            {
                // 退化为 WAL 也能跑，只是保留了 -wal/-shm 的故障面，故只告警、不抛出、不阻断启动。
                logger?.Warning("SQLite 日志模式未能固化为 DELETE（当前为 {Mode}）：{Path}", applied, databasePath);
            }

            return applied;
        }
        catch (Exception ex)
        {
            // PRAGMA 本身被拦截（如安全软件过滤驱动）也不应阻断启动，真正的故障留给后续迁移去暴露。
            logger?.Warning(ex, "固化 SQLite 日志模式失败（不阻断启动）：{Path}", databasePath);
            return null;
        }
        finally
        {
            // 清池：避免这次探测留下的连接继续持有文件句柄，干扰紧随其后的迁移。
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>判断给定日志模式是否为 DELETE（大小写不敏感；null/空视为否）。</summary>
    /// <param name="journalMode">PRAGMA 读回的模式字符串。</param>
    public static bool IsDelete(string? journalMode) =>
        string.Equals(journalMode, JOURNAL_MODE_DELETE, StringComparison.OrdinalIgnoreCase);

    /// <summary>执行只返回单个标量的命令，并把结果归一为 <c>ToLowerInvariant</c> 的字符串。</summary>
    /// <param name="connection">已打开的连接。</param>
    /// <param name="sql">要执行的 SQL。</param>
    private static string? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString()?.ToLowerInvariant();
    }
}
