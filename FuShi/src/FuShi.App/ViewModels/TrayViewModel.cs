using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FuShi.ViewModels;

/// <summary>
/// 托盘视图模型：把托盘菜单/双击动作转成命令，供 H.NotifyIcon 的 TaskbarIcon 绑定。
/// 具体动作（显示/隐藏悬浮窗、退出）由宿主以委托注入，本类不直接引用窗口与服务。
/// </summary>
public partial class TrayViewModel : ObservableObject
{
    private readonly Action _toggleWidget;
    private readonly Action _exit;

    /// <summary>构造托盘视图模型。</summary>
    /// <param name="toggleWidget">显示 / 隐藏悬浮窗。</param>
    /// <param name="exit">退出应用。</param>
    public TrayViewModel(Action toggleWidget, Action exit)
    {
        _toggleWidget = toggleWidget;
        _exit = exit;
    }

    /// <summary>显示 / 隐藏悬浮窗（菜单项与双击共用）。</summary>
    [RelayCommand]
    private void ToggleWidget() => _toggleWidget();

    /// <summary>退出应用。</summary>
    [RelayCommand]
    private void Exit() => _exit();
}
