namespace FuShi.Infrastructure.Data;

/// <summary>
/// 一次备份操作的结果（M5a）。备份失败<b>不抛异常</b>：启动路径与设置页都按结果对象处理，
/// 失败原因通过 <see cref="Error"/> 透出到界面/日志，绝不因备份失败阻断应用启动。
/// </summary>
/// <param name="Success">是否成功。</param>
/// <param name="FilePath">成功时的备份文件绝对路径；失败为 null。</param>
/// <param name="Error">失败原因；成功为 null。</param>
public sealed record BackupResult(bool Success, string? FilePath, string? Error)
{
    /// <summary>构造一个成功结果。</summary>
    /// <param name="filePath">备份文件路径。</param>
    public static BackupResult Ok(string filePath) => new(true, filePath, null);

    /// <summary>构造一个失败结果。</summary>
    /// <param name="error">失败原因。</param>
    public static BackupResult Fail(string error) => new(false, null, error);
}
