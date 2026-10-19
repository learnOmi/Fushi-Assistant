using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FuShi.Core;
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

        _hoverTimer = new DispatcherTimer();
        _hoverTimer.Tick += OnHoverDelayElapsed;

        // 倒计时刷新：UI 侧每秒推一次，ViewModel 保持纯 net8.0 可测、不依赖 WPF 计时器。
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UiConstants.COUNTDOWN_REFRESH_INTERVAL_MS)
        };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    /// <summary>每秒刷新一次视图模型（M2b 起据此更新倒计时/进度）。</summary>
    private void OnCountdownTick(object? sender, EventArgs e) => _viewModel.Refresh(DateTime.Now);

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
        // 双击 = 打开课表（设计 5.3）；与单击拖动区分开，命中时不做拖动。
        if (e.ClickCount == 2)
        {
            OpenScheduleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

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

    /// <summary>把当前位置交给视图模型持久化（写入 AppSetting 单行表）。</summary>
    public void SavePosition() => _viewModel.PersistPosition(Left, Top);

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
