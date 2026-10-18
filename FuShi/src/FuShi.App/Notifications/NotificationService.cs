using System.Windows;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using FuShi.UI;
using Serilog;

namespace FuShi.Notifications;

/// <summary>
/// 通知调度器：实现 <see cref="INotificationChannel"/>，把引擎派发的提醒按「免打扰 + 力度」策略
/// 落到具体通道上（设计文档 ADR-4 双通道）。
/// <list type="bullet">
/// <item>力度为 <see cref="ReminderStrength.Soft"/> → 只记日志，不打断（M2b 尚无图标动效）。</item>
/// <item>处于免打扰时段 → 降级为系统 Toast（保底通道，比自绘弹窗更克制）。</item>
/// <item>其余 → 自绘弹窗；力度为 <see cref="ReminderStrength.Strong"/> 时追加系统 Toast。</item>
/// </list>
/// 引擎在后台线程调用 <see cref="Dispatch"/>，本类负责编组到 UI 线程后再操作窗口。
/// </summary>
public sealed class NotificationService : INotificationChannel
{
    private readonly AppSettingRepository _settings;
    private readonly ToastNotificationChannel _toast;
    private readonly ILogger _logger;

    /// <summary>构造通知调度器。</summary>
    /// <param name="settings">设置仓储（读取免打扰时段）。</param>
    /// <param name="toast">系统 Toast 通道。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public NotificationService(
        AppSettingRepository settings,
        ToastNotificationChannel toast,
        ILogger logger)
    {
        _settings = settings;
        _toast = toast;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispatch(ReminderMessage message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Show(message);
            return;
        }

        // 引擎在后台线程 → 编组回 UI 线程（弹窗是 UI 资源）。
        dispatcher.BeginInvoke(new Action(() => Show(message)));
    }

    /// <summary>在 UI 线程上按策略选择通道并展示。</summary>
    /// <param name="message">提醒内容。</param>
    private void Show(ReminderMessage message)
    {
        if (message.Strength == ReminderStrength.Soft)
        {
            _logger.Information("轻提醒（仅落库不打断）：{Title} {Body}", message.Title, message.Body);
            return;
        }

        // 仓储内部 await 一律 ConfigureAwait(false)，故 UI 线程可安全同步等待这一次微小的读。
        var setting = _settings.GetAsync().GetAwaiter().GetResult();

        if (IsQuietNow(setting, DateTime.Now))
        {
            _logger.Information("处于免打扰时段，降级为系统 Toast：{Title}", message.Title);
            _toast.Show(message);
            return;
        }

        new ReminderPopupWindow(message.Title, message.Body).Show();

        if (message.Strength == ReminderStrength.Strong)
        {
            _toast.Show(message);
        }
    }

    /// <summary>
    /// 当前是否处于免打扰时段。支持跨午夜（如 23:00~07:00）：起止倒置时按「跨天」判定。
    /// </summary>
    /// <param name="setting">当前设置。</param>
    /// <param name="now">当前时刻。</param>
    private static bool IsQuietNow(AppSetting setting, DateTime now)
    {
        if (setting.QuietStart is null || setting.QuietEnd is null)
        {
            return false;
        }

        var start = setting.QuietStart.Value;
        var end = setting.QuietEnd.Value;
        var current = TimeOnly.FromDateTime(now);

        return start <= end
            ? current >= start && current < end
            : current >= start || current < end;
    }
}
