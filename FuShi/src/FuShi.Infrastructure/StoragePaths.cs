using System.IO;

namespace FuShi.Infrastructure;

/// <summary>
/// 存储与日志的落盘路径常量与推导（全部集中在 %APPDATA%\FuShi 下，便于用户备份/迁移）。
/// 把路径拼接与文件名集中在这里，避免业务代码里出现散落的字符串字面量。
/// </summary>
public static class StoragePaths
{
    /// <summary>应用数据目录名（%APPDATA% 下的子目录）。</summary>
    public const string APP_FOLDER_NAME = "FuShi";

    /// <summary>SQLite 数据库文件名。</summary>
    public const string DATABASE_FILE_NAME = "fushi.db";

    /// <summary>日志子目录名。</summary>
    public const string LOG_FOLDER_NAME = "logs";

    /// <summary>数据库备份子目录名（M5a）。</summary>
    public const string BACKUP_FOLDER_NAME = "backups";

    /// <summary>CSV 导出子目录名（M5a）。</summary>
    public const string EXPORT_FOLDER_NAME = "export";

    /// <summary>日志文件名模式（Serilog 按天滚动）。</summary>
    public const string LOG_FILE_TEMPLATE = "fushi-.log";

    /// <summary>日志文件保留天数。</summary>
    public const int LOG_RETAIN_DAYS = 14;

    /// <summary>应用数据根目录（%APPDATA%\FuShi）。</summary>
    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        APP_FOLDER_NAME);

    /// <summary>数据库文件绝对路径。</summary>
    public static string DatabasePath => Path.Combine(AppDataDirectory, DATABASE_FILE_NAME);

    /// <summary>日志目录绝对路径。</summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, LOG_FOLDER_NAME);

    /// <summary>备份目录绝对路径（M5a）。</summary>
    public static string BackupDirectory => Path.Combine(AppDataDirectory, BACKUP_FOLDER_NAME);

    /// <summary>CSV 导出目录绝对路径（M5a）。</summary>
    public static string ExportDirectory => Path.Combine(AppDataDirectory, EXPORT_FOLDER_NAME);

    /// <summary>SQLite 连接字符串。</summary>
    public static string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>确保应用数据目录存在。目录不可写时抛出异常（由上层决定如何提示）。</summary>
    public static void EnsureAppDataDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }
}
