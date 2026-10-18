using CommunityToolkit.WinUI.Notifications;
using FuShi.Core;
using FuShi.Infrastructure.Scheduling;
using Serilog;

namespace FuShi.Notifications;

/// <summary>
/// Windows 系统 Toast 通道（设计文档 ADR-4 的保底通道）。
/// <para>
/// 风险 6：未打包的桌面进程弹 Toast 需要应用用户模型 ID（AUMID）。本类用社区工具包的
/// <c>ToastNotificationManagerCompat</c>（它会代为注册 COM 激活器与 AUMID）尝试弹出；
/// <see cref="Probe"/> 在启动时做一次可用性探测并记日志，运行期任何失败都把本通道置为不可用，
/// 后续静默跳过并记警告——行为降级到「只有自绘弹窗」，但绝不吞异常、不影响主流程。
/// </para>
/// </summary>
public sealed class ToastNotificationChannel
{
    private readonly ILogger _logger;

    /// <summary>本通道当前是否可用；探测或首次弹出失败后置为 false。</summary>
    private bool _available;

    /// <summary>构造 Toast 通道。</summary>
    /// <param name="logger">Serilog 日志器。</param>
    public ToastNotificationChannel(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 启动探测：尝试获取通知器，判定非打包进程能否使用 Toast（风险 6 的验证点）。
    /// 结论写入日志；返回是否可用。
    /// </summary>
    public bool Probe()
    {
        try
        {
            ToastNotificationManagerCompat.CreateToastNotifier();
            _available = true;
            _logger.Information("Toast 通道可用（AUMID={Aumid}）", NotificationConstants.AUMID);
        }
        catch (Exception ex)
        {
            _available = false;
            _logger.Warning(ex, "Toast 通道不可用（AUMID={Aumid}），已降级为仅自绘弹窗", NotificationConstants.AUMID);
        }

        return _available;
    }

    /// <summary>弹出一条系统 Toast；失败则把通道置为不可用并记警告，不影响调用方。</summary>
    /// <param name="message">提醒内容。</param>
    public void Show(ReminderMessage message)
    {
        if (!_available)
        {
            return;
        }

        try
        {
            new ToastContentBuilder()
                .AddText(message.Title)
                .AddText(message.Body)
                .Show();
            _logger.Information("已弹出系统 Toast：{Title}", message.Title);
        }
        catch (Exception ex)
        {
            // 动画/注册/权限等任一环节失败都视为通道不可用：降级而非抛出。
            _available = false;
            _logger.Warning(ex, "弹出系统 Toast 失败，已降级为仅自绘弹窗");
        }
    }
}
