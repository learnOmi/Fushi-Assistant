using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FuShi.Core;

namespace FuShi.UI;

/// <summary>
/// 悬浮组件窗口：无边框、置顶、不进任务栏的圆角卡片。
/// 交互（设计文档 5.3）：悬停 200ms 自动展开；鼠标移开 300ms 自动收起；
/// 左键拖动移动位置，拖完持久化。
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly WidgetStateStore _store;
    private readonly DispatcherTimer _hoverTimer;

    /// <summary>当前是否处于展开态。</summary>
    private bool _expanded;

    /// <summary>延时任务的目标状态：true = 待展开，false = 待收起。</summary>
    private bool _pendingExpand;

    public WidgetWindow(WidgetStateStore store)
    {
        InitializeComponent();
        _store = store;

        _hoverTimer = new DispatcherTimer();
        _hoverTimer.Tick += OnHoverDelayElapsed;

        UpdateDateText();
    }

    private void UpdateDateText()
    {
        var now = DateTime.Now;
        DateText.Text = $"{now:M月d日} 周{"日一二三四五六"[(int)now.DayOfWeek]}";
    }

    // ---------- 悬停展开 / 移开收起 ----------
    private void OnHoverEnter(object sender, MouseEventArgs e) => ScheduleExpand(true);

    private void OnHoverLeave(object sender, MouseEventArgs e) => ScheduleExpand(false);

    /// <summary>
    /// 把展开/收起排到一个延时任务上。计时期内发生反向操作会重置计时，
    /// 因此鼠标在边缘反复进出时不会来回抖动。
    /// </summary>
    /// <param name="expand">true = 目标为展开；false = 目标为收起。</param>
    private void ScheduleExpand(bool expand)
    {
        _pendingExpand = expand;
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(
            expand ? UiConstants.HOVER_EXPAND_DELAY_MS : UiConstants.HOVER_COLLAPSE_DELAY_MS);
        _hoverTimer.Start();
    }

    private void OnHoverDelayElapsed(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();

        // 收起前复核光标位置：拖动期间鼠标被窗口捕获，MouseLeave 可能不会触发，
        // 若此时光标仍压在组件上就保持展开，避免误收起。
        if (!_pendingExpand && IsCursorInside()) return;

        SetExpanded(_pendingExpand);
    }

    /// <summary>按目标状态切换展开态。M1 的展开内容是空课表态，不承载真实课表。</summary>
    /// <param name="expanded">true = 展开；false = 收起。</param>
    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        Expanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 光标是否落在组件范围内。用 WPF 的坐标（设备无关单位）判断，
    /// 从而在多 DPI 显示器下不会因物理/逻辑像素换算而误判。
    /// </summary>
    private bool IsCursorInside()
    {
        var p = Mouse.GetPosition(this);
        return p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
    }

    // ---------- 左键拖动移动位置 ----------
    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        _hoverTimer.Stop(); // 拖动期间不做展开/收起判定

        Win32.GetCursorPos(out var before);
        DragMove();
        Win32.GetCursorPos(out var after);

        double moved = Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y);
        if (moved >= SystemParameters.MinimumHorizontalDragDistance + SystemParameters.MinimumVerticalDragDistance)
        {
            SavePosition(); // 拖完记住位置
        }

        // 拖动时鼠标被窗口捕获，MouseLeave 收不到；拖完按光标真实位置补一次判定
        ScheduleExpand(IsCursorInside());
    }

    public void SavePosition() => _store.Save(Left, Top);
}
