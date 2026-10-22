using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FuShi.Core;
using FuShi.ViewModels;

namespace FuShi.UI;

/// <summary>
/// 主窗口：课表录入与查看的宿主。关闭 = 隐藏（不退出进程，设计 4.x）；单例由宿主持有。
/// 视图只做"弹框"这类偶然行为与生命周期收尾，业务逻辑都在 <see cref="MainWindowViewModel"/>。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>是否允许真正关闭（退出流程置位，用于绕过"关闭即隐藏"）。</summary>
    private bool _allowClose;

    /// <summary>构造。</summary>
    /// <param name="viewModel">主窗口视图模型。</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>句柄就绪后挂无边框窗口适配：最大化边界修正 + DWM 圆角（Win10 上圆角失败即保持直角，属预期降级）。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e) => WindowChromeHelper.Attach(this);

    /// <summary>标题栏：最小化。</summary>
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    /// <summary>标题栏：最大化 / 还原切换（图标由样式按 WindowState 切换）。</summary>
    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    /// <summary>标题栏：关闭。走系统关闭语义，由 <see cref="OnClosing"/> 转为收起到后台。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>每次显示都重新载入（自举阶段 + 拉最新课表），保证数据不过期。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    /// <summary>
    /// 阶段徽标点击（M4a）：在徽标下方弹出阶段下拉。
    /// <para>
    /// 菜单项按当前阶段列表<b>动态构建</b>，而不是把 <c>ItemsSource</c> 与「管理阶段…」这一固定项混排——
    /// <see cref="ContextMenu"/> 的 Items 与 ItemsSource 互斥，动态构建是同时容纳「阶段项 + 分隔线 + 固定项」最简单可靠的做法。
    /// 菜单项的 <c>Command</c> 取自视图模型，明细绑定只在这里做。
    /// </para>
    /// </summary>
    private void OnPhaseBadgeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = PhaseBadgeButton,
            Placement = PlacementMode.Bottom,
            // 显式 false：菜单在选中项或点外部后立即收起，避免残留在界面上（M3 托盘菜单的同类教训）。
            StaysOpen = false,
        };

        foreach (var phase in _viewModel.PhaseOptions)
        {
            menu.Items.Add(new MenuItem
            {
                Header = phase.Name,
                IsCheckable = true,
                IsChecked = phase.IsActive,
                Command = _viewModel.SwitchPhaseCommand,
                CommandParameter = phase.Id,
            });
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(new MenuItem
        {
            Header = WindowText.PHASE_MENU_MANAGE,
            Command = _viewModel.OpenPhaseManagerCommand,
        });

        menu.IsOpen = true;
    }

    /// <summary>关闭按钮 → 收起到后台（真正退出由宿主调用 <see cref="ForceClose"/>）。</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    /// <summary>允许并执行真正关闭（应用退出时调用）。</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }
}
