using System.Windows.Controls;

namespace FuShi.UI.Views;

/// <summary>
/// 事项页（M4c）：纯展示视图，所有逻辑与数据在 <c>ItemsViewModel</c>。
/// 行内按钮通过 RelativeSource 取本控件的 DataContext 调用命令。
/// </summary>
public partial class ItemsView : UserControl
{
    /// <summary>构造。</summary>
    public ItemsView() => InitializeComponent();
}
