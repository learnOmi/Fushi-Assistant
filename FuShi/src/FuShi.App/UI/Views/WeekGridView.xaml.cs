using System.Windows.Controls;

namespace FuShi.UI.Views;

/// <summary>
/// 周网格视图（UserControl）：7 列 × N 行，点格子进入编辑 / 新建。
/// 纯展示：数据经绑定取自 <see cref="FuShi.ViewModels.WeekGridViewModel"/>，视图不做任何业务计算。
/// </summary>
public partial class WeekGridView : UserControl
{
    /// <summary>构造。</summary>
    public WeekGridView() => InitializeComponent();
}
