namespace FuShi.Infrastructure.Data;

/// <summary>
/// 数据库备份的命名与保留策略常量（M5a）。
/// 集中在此，避免文件名字面量与保留数量散落在服务代码里。
/// </summary>
public static class BackupConstants
{
    /// <summary>自动备份文件名前缀（与手动备份前缀互补）。</summary>
    public const string AUTO_FILE_PREFIX = "fushi-";

    /// <summary>手动备份文件名前缀；带此前缀的文件永不参与自动清理。</summary>
    public const string MANUAL_FILE_PREFIX = "fushi-manual-";

    /// <summary>备份文件扩展名。</summary>
    public const string FILE_EXTENSION = ".db";

    /// <summary>自动备份文件名格式：一天一份（{0} = 日期）。</summary>
    public const string AUTO_FILE_FORMAT = AUTO_FILE_PREFIX + "{0:yyyyMMdd}" + FILE_EXTENSION;

    /// <summary>手动备份文件名格式：精确到秒（{0} = 时刻）。</summary>
    public const string MANUAL_FILE_FORMAT = MANUAL_FILE_PREFIX + "{0:yyyyMMdd_HHmmss}" + FILE_EXTENSION;

    /// <summary>手动备份同名冲突时的追加序号格式（{0} = 序号）。</summary>
    public const string MANUAL_FILE_CONFLICT_SUFFIX_FORMAT = "-{0}" + FILE_EXTENSION;

    /// <summary>自动备份的保留份数；超出的按文件名（即日期）倒序删除。</summary>
    public const int AUTO_RETAIN_COUNT = 7;

    /// <summary>枚举备份文件的通配符（先按通配符取，再按前缀精筛）。</summary>
    public const string FILE_SEARCH_PATTERN = "*" + FILE_EXTENSION;

    /// <summary>
    /// SQLite 一致性快照语句。用 <c>VACUUM INTO</c> 而不是直接复制文件：
    /// DELETE 日志模式下进程被强杀会残留 <c>-journal</c>，直接拷贝可能拿到不一致的页；
    /// <c>VACUUM INTO</c> 在 SQLite 内部生成一致性快照且产物是单个文件（{0} = 目标路径）。
    /// </summary>
    public const string VACUUM_INTO_FORMAT = "VACUUM INTO '{0}'";
}
