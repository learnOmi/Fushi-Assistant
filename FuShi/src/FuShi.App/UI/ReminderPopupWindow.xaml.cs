using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FuShi.Core;

namespace FuShi.UI;

/// <summary>
/// 自绘提醒弹窗（设计文档 ADR-4 的主通道）。
/// 职责是**纯视图**：展示引擎给的标题/正文，到点自动消失，点击即刻关闭。
/// 位置固定在主屏工作区右下角，避免遮挡悬浮组件与用户操作区。
/// </summary>
public partial class ReminderPopupWindow : Window
{
    /// <summary>自动消失计时器。</summary>
    private readonly DispatcherTimer _dismissTimer;

    /// <summary>构造弹窗。</summary>
    /// <param name="title">标题（通常是科目名）。</param>
    /// <param name="body">正文（如「14:00 开始」）。</param>
    public ReminderPopupWindow(string title, string body)
    {
        InitializeComponent();

        TitleText.Text = title;
        BodyText.Text = body;

        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(NotificationConstants.POPUP_AUTO_DISMISS_MILLISECONDS),
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

    /// <summary>自动消失。</summary>
    private void OnAutoDismiss(object? sender, EventArgs e) => Close();

    /// <summary>点击弹窗即关闭。</summary>
    private void OnPopupClick(object sender, MouseButtonEventArgs e) => Close();

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        _dismissTimer.Tick -= OnAutoDismiss;
        base.OnClosed(e);
    }
}
