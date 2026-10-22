using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FuShi.Core;
using FuShi.Domain.Layout;
using FuShi.Domain.Scheduling;
using FuShi.ViewModels;

namespace FuShi.UI;

/// <summary>
/// 悬浮组件窗口：无边框、置顶、不进任务栏的圆角卡片。
/// 只承担**纯视图职责**——拖动（DragMove）、悬停 200/300ms 计时、命中判定、倒计时定时器；
/// 数据与状态全部经绑定取自 <see cref="WidgetViewModel"/>（交互时序见设计文档 5.3）。
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _countdownTimer;

    /// <summary>吸附位移的缓动计时器（松手后平滑滑到贴边位置，M5 视觉增强）。</summary>
    private readonly DispatcherTimer _snapTimer;

    /// <summary>未吸附时的卡片圆角（XAML 初始值），吸附时按命中边改成直角，拖走时还原。</summary>
    private readonly CornerRadius _restingCornerRadius;

    /// <summary>当前已命中的吸附边；<see cref="DockEdges.None"/> 表示未吸附。</summary>
    private DockEdges _dockedEdges;

    /// <summary>缓动起点时刻与起止坐标。</summary>
    private DateTime _snapStartedAt;
    private double _snapFromLeft;
    private double _snapFromTop;
    private double _snapToLeft;
    private double _snapToTop;

    /// <summary>延时任务的目标状态：true = 待展开，false = 待收起。</summary>
    private bool _pendingExpand;

    /// <summary>请求打开课表（双击组件或点击展开态「打开课表」按钮时触发，由宿主接管）。</summary>
    public event EventHandler? OpenScheduleRequested;

    /// <summary>构造窗口。</summary>
    /// <param name="viewModel">视图模型（数据与状态来源）。</param>
    public WidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        _restingCornerRadius = Root.CornerRadius;

        _hoverTimer = new DispatcherTimer();
        _hoverTimer.Tick += OnHoverDelayElapsed;

        // 倒计时刷新：UI 侧每秒推一次，ViewModel 保持纯 net8.0 可测、不依赖 WPF 计时器。
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UiConstants.COUNTDOWN_REFRESH_INTERVAL_MS)
        };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();

        _snapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UiConstants.WIDGET_SNAP_ANIMATION_INTERVAL_MS)
        };
        _snapTimer.Tick += OnSnapTick;

        // 贴底时高度变化要向上生长（见 OnSizeChanged），否则展开面板会伸到屏幕外。
        SizeChanged += OnWidgetSizeChanged;
    }

    /// <summary>每秒刷新一次视图模型（M2b 起据此更新倒计时/进度）。</summary>
    private void OnCountdownTick(object? sender, EventArgs e) => _viewModel.Refresh(DateTime.Now);

    // ---------- 悬停展开 / 移开收起 ----------
    private void OnHoverEnter(object sender, MouseEventArgs e)
    {
        _viewModel.RecordInteraction(DateTime.Now); // 悬停即「醒过来」，让困倦表情立刻退回待机
        ScheduleExpand(true);
    }

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
        if (!_pendingExpand && IsCursorInside())
        {
            return;
        }

        SetExpanded(_pendingExpand);
    }

    /// <summary>按目标状态切换展开态（状态落在视图模型，视图据绑定切换可见性）。</summary>
    /// <param name="expanded">true = 展开；false = 收起。</param>
    private void SetExpanded(bool expanded)
    {
        if (_viewModel.IsExpanded == expanded)
        {
            return;
        }

        _viewModel.IsExpanded = expanded;
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
        _viewModel.RecordInteraction(DateTime.Now); // 按下即交互，拖动/双击都算

        // 双击 = 打开课表（设计 5.3）；与单击拖动区分开，命中时不做拖动。
        if (e.ClickCount == 2)
        {
            OpenScheduleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _hoverTimer.Stop(); // 拖动期间不做展开/收起判定

        // 拖动一律先收起：吸附判定必须用「常驻态卡片」的高度，
        // 否则展开面板的底边会被当成窗口底边，贴底后一收起卡片就悬空。
        SetExpanded(false);

        // 开始新拖动：停掉上一次吸附的缓动、还原卡片外观，避免拖到一半被旧动画抢位置。
        _snapTimer.Stop();
        RestoreDockAppearance();

        Win32.GetCursorPos(out var before);
        DragMove();
        Win32.GetCursorPos(out var after);

        double moved = Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y);
        if (moved >= SystemParameters.MinimumHorizontalDragDistance + SystemParameters.MinimumVerticalDragDistance)
        {
            var snap = SnapToWorkAreaEdge();
            if (snap.Edges != DockEdges.None)
            {
                ApplyDockAppearance(snap.Edges);
                FlashEdges(snap.Edges);
                // 落库直接写最终坐标，不写缓动中间值（否则退出时可能存到半路位置）。
                _viewModel.PersistPosition(snap.Left, snap.Top);
                StartSnapAnimation(snap.Left, snap.Top);
            }
            else
            {
                SavePosition();
            }
        }

        // 拖动时鼠标被窗口捕获，MouseLeave 收不到；拖完按光标真实位置补一次判定
        ScheduleExpand(IsCursorInside());
    }

    /// <summary>
    /// 拖动结束后的贴边吸附（M5b）：距当前显示器工作区边缘 ≤ <see cref="WidgetDock.SNAP_THRESHOLD"/> 时贴合。
    /// 判定用纯函数 <see cref="WidgetDock.Snap"/>，这里只负责把工作区换算到窗口坐标系。
    /// </summary>
    /// <returns>吸附结果（坐标 + 命中的边）；未命中任何边时 <see cref="WidgetSnap.Edges"/> 为 None。</returns>
    private WidgetSnap SnapToWorkAreaEdge()
    {
        // 收起后高度变化需要一次同步布局，否则这里可能读到收起前的 ActualHeight。
        UpdateLayout();

        var workArea = WindowChromeHelper.GetWorkAreaInWindowCoordinates(this);
        return WidgetDock.Snap(
            Left,
            Top,
            ActualWidth,
            ActualHeight,
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom);
    }

    /// <summary>
    /// 高度变化时保持「贴底的边」不动：若变化前窗口底边就贴着工作区底边（容差 1 DIP），
    /// 则按新高度反推 Top，使展开面板向上生长、收起时卡片回落到贴边位置。
    /// <para>
    /// 用「变化前的底边」自动判断，而不是依赖 <see cref="_dockedEdges"/>：
    /// 重启后该字段已重置，但只要上次保存的位置是贴底的，这个判定依然成立，行为保持一致。
    /// </para>
    /// </summary>
    private void OnWidgetSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged)
        {
            return;
        }

        // 吸附缓动期间 Top 由动画逐帧写入，此时不抢位置（缓动结束后自然落到底边）。
        if (_snapTimer.IsEnabled)
        {
            return;
        }

        var workArea = WindowChromeHelper.GetWorkAreaInWindowCoordinates(this);
        var previousBottom = Top + e.PreviousSize.Height;
        if (Math.Abs(previousBottom - workArea.Bottom) > UiConstants.WIDGET_BOTTOM_ANCHOR_TOLERANCE)
        {
            return;
        }

        Top = workArea.Bottom - ActualHeight;
    }

    /// <summary>
    /// 启动吸附位移的缓动：160ms ease-out 从松手位置滑到贴边位置。
    /// 用 <see cref="DispatcherTimer"/> 逐帧推进而非直接动画 <c>Window.Left/Top</c>：
    /// 窗口坐标不是为动画设计的依赖属性，逐帧赋值行为更可预期，也便于被下一次拖动立即打断。
    /// </summary>
    /// <param name="left">目标左边界。</param>
    /// <param name="top">目标上边界。</param>
    private void StartSnapAnimation(double left, double top)
    {
        _snapFromLeft = Left;
        _snapFromTop = Top;
        _snapToLeft = left;
        _snapToTop = top;
        _snapStartedAt = DateTime.UtcNow;
        _snapTimer.Start();
        PlayBounce();
    }

    /// <summary>缓动帧：ease-out cubic 插值，走满后精确落到目标坐标。</summary>
    private void OnSnapTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _snapStartedAt).TotalMilliseconds;
        var progress = Math.Min(1.0, elapsed / UiConstants.WIDGET_SNAP_ANIMATION_DURATION_MS);
        var eased = 1 - Math.Pow(1 - progress, 3);

        Left = _snapFromLeft + (_snapToLeft - _snapFromLeft) * eased;
        Top = _snapFromTop + (_snapToTop - _snapFromTop) * eased;

        if (progress >= 1.0)
        {
            _snapTimer.Stop();
            Left = _snapToLeft;
            Top = _snapToTop;
        }
    }

    /// <summary>
    /// 按命中边调整卡片外观：贴住的那一侧圆角改直角，另一侧保留圆角，
    /// 让卡片看起来是「长在屏幕边缘上」而不是浮在旁边。拖走时由 <see cref="RestoreDockAppearance"/> 还原。
    /// </summary>
    private void ApplyDockAppearance(DockEdges edges)
    {
        var topLeft = _restingCornerRadius.TopLeft;
        var topRight = _restingCornerRadius.TopRight;
        var bottomRight = _restingCornerRadius.BottomRight;
        var bottomLeft = _restingCornerRadius.BottomLeft;

        if (edges.HasFlag(DockEdges.Left))
        {
            topLeft = 0;
            bottomLeft = 0;
        }

        if (edges.HasFlag(DockEdges.Right))
        {
            topRight = 0;
            bottomRight = 0;
        }

        if (edges.HasFlag(DockEdges.Top))
        {
            topLeft = 0;
            topRight = 0;
        }

        if (edges.HasFlag(DockEdges.Bottom))
        {
            bottomLeft = 0;
            bottomRight = 0;
        }

        Root.CornerRadius = new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
        _dockedEdges = edges;
    }

    /// <summary>还原未吸附时的圆角（开始拖动时调用）。</summary>
    private void RestoreDockAppearance()
    {
        if (_dockedEdges == DockEdges.None)
        {
            return;
        }

        Root.CornerRadius = _restingCornerRadius;
        _dockedEdges = DockEdges.None;
    }

    /// <summary>沿命中的边闪一条高亮线，250ms 淡出（贴角落则两条同时亮）。</summary>
    private void FlashEdges(DockEdges edges)
    {
        FlashEdge(EdgeLeft, edges.HasFlag(DockEdges.Left));
        FlashEdge(EdgeRight, edges.HasFlag(DockEdges.Right));
        FlashEdge(EdgeTop, edges.HasFlag(DockEdges.Top));
        FlashEdge(EdgeBottom, edges.HasFlag(DockEdges.Bottom));
    }

    /// <summary>单条高亮线：先取消旧动画，命中则从全亮淡到 0。</summary>
    private static void FlashEdge(Border edge, bool flash)
    {
        edge.BeginAnimation(OpacityProperty, null);
        edge.Opacity = 0;
        if (!flash)
        {
            return;
        }

        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(UiConstants.WIDGET_SNAP_FLASH_DURATION_MS))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        edge.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// 吸附回弹：整体缩放到 0.97 再弹回 1.0（带一次轻微过冲）。
    /// 键帧用 <see cref="SplineDoubleKeyFrame"/> 表达弹簧曲线（项目约定：不用普通 DoubleAnimation 模拟弹簧）。
    /// </summary>
    private void PlayBounce()
    {
        RootScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, CreateBounce());
        RootScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, CreateBounce());
    }

    /// <summary>构造一次回弹键帧（X/Y 各用一份，避免同一时间线被两个属性共享）。</summary>
    private static DoubleAnimationUsingKeyFrames CreateBounce()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(UiConstants.WIDGET_SNAP_BOUNCE_DURATION_MS)
        };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            UiConstants.WIDGET_SNAP_BOUNCE_SCALE,
            KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            UiConstants.WIDGET_SNAP_BOUNCE_OVERSHOOT,
            KeyTime.FromPercent(0.45),
            new KeySpline(0.2, 0.8, 0.3, 1.0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            1.0,
            KeyTime.FromPercent(1.0),
            new KeySpline(0.4, 0.0, 0.2, 1.0)));
        return animation;
    }

    /// <summary>
    /// 把当前位置交给视图模型持久化（写入 AppSetting 单行表）。
    /// 缓动进行中落库的是目标坐标，而不是动画中间值。
    /// </summary>
    public void SavePosition() => _viewModel.PersistPosition(
        _snapTimer.IsEnabled ? _snapToLeft : Left,
        _snapTimer.IsEnabled ? _snapToTop : Top);

    /// <summary>展开态「打开课表」按钮：请求宿主唤起主窗口。</summary>
    private void OnOpenSchedule(object sender, RoutedEventArgs e) =>
        OpenScheduleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 推入新的课表时段表（由宿主在启动时、以及引擎报告数据变更时调用）。
    /// 视图本身不做数据计算，只把数据转交给视图模型。
    /// </summary>
    /// <param name="slots">已展开的时段表（升序）。</param>
    public void ApplySchedule(IReadOnlyList<TimelineSlot> slots) => _viewModel.ApplySchedule(slots);
}
