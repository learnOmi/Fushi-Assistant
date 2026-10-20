using System.Windows.Controls;

namespace FuShi.UI.Views;

/// <summary>
/// 设置页视图（UserControl）：不透明度 / 免打扰 / 提前提醒 / 时区 / 主题。
/// 纯展示：数据与命令经绑定取自 <see cref="FuShi.ViewModels.SettingsViewModel"/>，视图不做业务计算。
/// DataContext 由宿主显式绑定（见 MainWindow.xaml），不依赖继承。
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>构造。</summary>
    public SettingsView() => InitializeComponent();
}
