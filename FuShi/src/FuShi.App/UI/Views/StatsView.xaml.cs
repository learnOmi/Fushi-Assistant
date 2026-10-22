using System.Windows.Controls;

namespace FuShi.UI.Views;

/// <summary>
/// 统计页（M4b）：区间切换 + 数字卡 + 科目横条 + 最近 7 天柱状条。
/// 所有读数来自 <c>StatsViewModel</c> 的投影，视图不参与聚合。
/// </summary>
public partial class StatsView : UserControl
{
    /// <summary>构造视图。</summary>
    public StatsView()
    {
        InitializeComponent();
    }
}
