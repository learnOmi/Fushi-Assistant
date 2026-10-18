using System.IO;
using Serilog;

namespace FuShi.Infrastructure;

/// <summary>
/// Serilog 装配（取舍见 M2 实施计划第八节②）。
/// 落文件到 %APPDATA%\FuShi\logs，按天滚动、保留固定天数。
/// 引入日志的直接动因：DST 顺延、补偿判决、引擎异常都需要留痕（user_rules 也要求不吞异常）。
/// </summary>
public static class LoggingSetup
{
    /// <summary>创建全局日志器。</summary>
    public static ILogger Create()
    {
        Directory.CreateDirectory(StoragePaths.LogDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(StoragePaths.LogDirectory, StoragePaths.LOG_FILE_TEMPLATE),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: StoragePaths.LOG_RETAIN_DAYS)
            .CreateLogger();
    }
}
