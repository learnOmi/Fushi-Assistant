using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace FuShi.Infrastructure.Platform;

/// <summary>
/// 开机自启的执行结果。失败时带回可展示的原因，调用方（设置页 / 启动校验）据此回滚开关或写日志，
/// 而不是静默吞掉——注册表写入可能因策略或安全软件被拒绝，用户需要知道。
/// </summary>
/// <param name="Success">是否成功完成期望动作。</param>
/// <param name="Error">失败原因（成功时为 <c>null</c>）。</param>
public sealed record AutostartResult(bool Success, string? Error)
{
    /// <summary>成功。</summary>
    public static AutostartResult Ok() => new(true, null);

    /// <summary>失败，带回原因。</summary>
    /// <param name="error">失败原因。</param>
    public static AutostartResult Fail(string error) => new(false, error);
}

/// <summary>
/// 开机自启服务（M4d）：以 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> 的
/// <c>FuShi</c> 值为准。选 HKCU 而非计划任务/启动文件夹：无需管理员权限、可编程校验、
/// 删除即生效，符合自用工具"拷走能跑"的定位。
/// <para>
/// 约定「数据库是唯一事实来源」：设置页先落库再调用本服务，写注册表失败时由调用方回滚开关；
/// 启动时再用 <see cref="Sync"/> 把注册表纠正到库中状态（用户移动发布目录后路径会变）。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutostartService
{
    /// <summary>注册表 Run 子键路径（HKCU 下）。</summary>
    public const string REGISTRY_RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>本应用在 Run 键下的值名。</summary>
    public const string REGISTRY_VALUE_NAME = "FuShi";

    /// <summary>注册表访问被拒绝时的提示（带系统消息占位）。</summary>
    public const string ERROR_ACCESS_DENIED_FORMAT = "无法写入开机启动项（注册表访问被拒绝）：{0}";

    /// <summary>注册表读写失败时的提示（带系统消息占位）。</summary>
    public const string ERROR_REGISTRY_IO_FORMAT = "无法写入开机启动项（注册表读写失败）：{0}";

    /// <summary>取不到当前进程路径时的提示。</summary>
    public const string ERROR_EXECUTABLE_PATH_UNKNOWN = "无法确定当前程序路径，开机自启未生效";

    /// <summary>
    /// 取当前进程的可执行文件全路径。
    /// 单文件发布时 <see cref="Assembly.Location"/> 为空，必须优先用 <see cref="Environment.ProcessPath"/>。
    /// </summary>
    /// <returns>可执行文件全路径；两者都取不到时返回 <c>null</c>。</returns>
    public static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        var location = Assembly.GetEntryAssembly()?.Location;
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    /// <summary>
    /// 把可执行文件路径规范成注册表命令值：全路径 + 双引号。
    /// 引号是必需的——发布目录或用户名带空格时，Windows 会把未加引号的路径截断成第一条参数。
    /// </summary>
    /// <param name="executablePath">可执行文件路径。</param>
    /// <returns>带引号的全路径命令。</returns>
    public static string BuildCommand(string executablePath) =>
        string.Concat("\"", Path.GetFullPath(executablePath), "\"");

    /// <summary>
    /// 写入或删除自启项（不做路径比对，直接按 <paramref name="enabled"/> 覆盖）。
    /// </summary>
    /// <param name="enabled">true = 写入；false = 删除（不存在时静默成功）。</param>
    /// <param name="executablePath">可执行文件路径（<paramref name="enabled"/> 为 true 时使用）。</param>
    /// <returns>执行结果；不抛异常。</returns>
    public AutostartResult Apply(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_RUN_KEY);

            if (enabled)
            {
                key.SetValue(REGISTRY_VALUE_NAME, BuildCommand(executablePath), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(REGISTRY_VALUE_NAME, throwOnMissingValue: false);
            }

            return AutostartResult.Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return AutostartResult.Fail(string.Format(ERROR_ACCESS_DENIED_FORMAT, ex.Message));
        }
        catch (SecurityException ex)
        {
            return AutostartResult.Fail(string.Format(ERROR_ACCESS_DENIED_FORMAT, ex.Message));
        }
        catch (IOException ex)
        {
            return AutostartResult.Fail(string.Format(ERROR_REGISTRY_IO_FORMAT, ex.Message));
        }
    }

    /// <summary>
    /// 按当前进程的可执行文件路径写入 / 删除自启项（设置页使用；路径取自
    /// <see cref="Environment.ProcessPath"/>，兼容单文件发布）。
    /// </summary>
    /// <param name="enabled">true = 写入；false = 删除。</param>
    /// <returns>执行结果；取不到进程路径时返回失败。</returns>
    public AutostartResult ApplyForCurrentProcess(bool enabled)
    {
        var executablePath = GetExecutablePath();
        return executablePath is null
            ? AutostartResult.Fail(ERROR_EXECUTABLE_PATH_UNKNOWN)
            : Apply(enabled, executablePath);
    }

    /// <summary>
    /// 把注册表纠正到期望状态：开启时若值缺失或与当前 exe 路径不一致则重写；关闭时若值存在则删除。
    /// 启动时调用一次，处理"用户移动了发布目录""手动删了注册表项"等与库不一致的情况。
    /// </summary>
    /// <param name="enabled">库中保存的自启开关。</param>
    /// <param name="executablePath">当前进程的可执行文件路径。</param>
    /// <returns>执行结果；无需改动或改成功都返回成功。</returns>
    public AutostartResult Sync(bool enabled, string executablePath)
    {
        var desired = BuildCommand(executablePath);
        var current = ReadCommand();

        if (enabled && string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
        {
            return AutostartResult.Ok();
        }

        if (!enabled && current is null)
        {
            return AutostartResult.Ok();
        }

        return Apply(enabled, executablePath);
    }

    /// <summary>读取注册表中已登记的命令值（不存在或读取失败时返回 <c>null</c>）。</summary>
    /// <returns>注册表值；无值时为 <c>null</c>。</returns>
    public string? ReadCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_RUN_KEY, writable: false);
            return key?.GetValue(REGISTRY_VALUE_NAME) as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // 只读探测失败等同于"读不到值"，交给 Apply/Sync 走写入分支并回报真实错误。
            return null;
        }
    }
}
