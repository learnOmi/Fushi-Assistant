using System.ComponentModel;
using System.Windows;
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

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>每次显示都重新载入（自举阶段 + 拉最新课表），保证数据不过期。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

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
