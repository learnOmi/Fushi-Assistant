using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Infrastructure.Scheduling;

namespace FuShi.UI;

/// <summary>
/// 自绘提醒弹窗（设计文档 ADR-4 的主通道，M3b 接动作）。
/// 职责仍是**纯视图**：
/// <list type="bullet">
/// <item>有归属实例（<c>OccurrenceId</c> 非空）→ 展示「开始 / 稍后 / 跳过」三按钮；
///       用户选择或超时未响应时，经注入的回调把动作交还上层（<c>NotificationService</c> → <c>ReminderResponseService</c>）。</item>
/// <item>汇总类通知（<c>OccurrenceId</c> 为空）→ 退化为无按钮的哑壳，按时自动关闭。</item>
/// </list>
/// 位置固定在主屏工作区右下角，避免遮挡悬浮组件与用户操作区。
/// </summary>
public partial class ReminderPopupWindow : Window
{
    /// <summary>自动消失计时器（带动作时按「未响应」超时，哑壳按普通自动关闭时长）。</summary>
    private readonly DispatcherTimer _dismissTimer;

    /// <summary>动作回调（由 <c>NotificationService</c> 注入）；哑壳弹窗为 null。</summary>
    private readonly Action<ReminderAction>? _onAction;

    /// <summary>本弹窗是否带动作按钮（= 有归属实例且注入了回调）。</summary>
    private readonly bool _hasActions;

    /// <summary>用户已选动作；null 表示尚未选择（关闭时按「未响应」补记）。</summary>
    private ReminderAction? _chosen;

    /// <summary>构造弹窗。</summary>
    /// <param name="message">引擎派发的提醒内容（标题 / 正文 / 归属实例）。</param>
    /// <param name="onAction">用户动作回调；汇总类通知传 null 表示无交互。</param>
    public ReminderPopupWindow(ReminderMessage message, Action<ReminderAction>? onAction)
    {
        InitializeComponent();

        TitleText.Text = message.Title;
        BodyText.Text = message.Body;

        _onAction = onAction;
        _hasActions = message.OccurrenceId is not null && onAction is not null;

        // 三按钮仅在有归属实例时出现；「稍后」文案带上延后天数（取领域默认值，避免硬编码）。
        if (_hasActions)
        {
            SnoozeButton.Content = string.Format(ReminderText.ACTION_SNOOZE_FORMAT, TimeDefaults.DEFAULT_SNOOZE_MINUTES);
            ActionsPanel.Visibility = Visibility.Visible;
        }

        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                _hasActions
                    ? NotificationConstants.POPUP_NO_RESPONSE_MILLISECONDS
                    : NotificationConstants.POPUP_AUTO_DISMISS_MILLISECONDS),
        };
        _dismissTimer.Tick += OnAutoDismiss;
        _dismissTimer.Start();

        // 首次布局完成后再按真实高度落地到右下角（SizeToContent=Height，Show 前拿不到高度）。
        Loaded += OnLoaded;
    }

    /// <summary>定位到主屏工作区右下角。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - NotificationConstants.POPUP_MARGIN;
        Top = area.Bottom - ActualHeight - NotificationConstants.POPUP_MARGIN;
    }

    /// <summary>自动消失（带动作时到点即视为「未响应」）。</summary>
    private void OnAutoDismiss(object? sender, EventArgs e) => Close();

    /// <summary>「开始」按钮。</summary>
    private void OnStart(object sender, RoutedEventArgs e) => Choose(ReminderAction.Start);

    /// <summary>「稍后」按钮。</summary>
    private void OnSnooze(object sender, RoutedEventArgs e) => Choose(ReminderAction.Snooze);

    /// <summary>「跳过」按钮。</summary>
    private void OnSkip(object sender, RoutedEventArgs e) => Choose(ReminderAction.Skip);

    /// <summary>记录用户选择并关闭（关闭时由 <see cref="OnClosed"/> 统一回调）。</summary>
    /// <param name="action">用户动作。</param>
    private void Choose(ReminderAction action)
    {
        _chosen = action;
        Close();
    }

    /// <summary>点击弹窗空白处即关闭；落在按钮内的点击由按钮自行处理，此处忽略避免抢动作。</summary>
    private void OnPopupClick(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource))
        {
            return;
        }

        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        _dismissTimer.Tick -= OnAutoDismiss;

        // 统一规则：关闭时若尚未选择任何动作，带动作的弹窗回记一次「未响应」
        // （涵盖 30s 超时自动收起与点击空白处关闭两种路径）。
        if (_hasActions && _chosen is null)
        {
            _chosen = ReminderAction.NoResponse;
        }

        if (_chosen is not null)
        {
            _onAction?.Invoke(_chosen.Value);
        }

        base.OnClosed(e);
    }

    /// <summary>判断点击源是否落在某个按钮内（沿可视树向上找 <see cref="ButtonBase"/>）。</summary>
    /// <param name="source">事件的 <c>OriginalSource</c>。</param>
    private static bool IsWithinButton(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }

            // 非可视元素（如 TextBlock 内的 Run）不在可视树上，无法向上取父级 → 判定为不在按钮内。
            if (current is not Visual)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
