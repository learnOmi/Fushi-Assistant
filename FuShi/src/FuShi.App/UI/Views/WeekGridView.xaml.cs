using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FuShi.ViewModels;

namespace FuShi.UI.Views;

/// <summary>
/// 周网格视图（UserControl）：7 列 × N 行，点格子进入编辑 / 新建。
/// 纯展示：数据经绑定取自 <see cref="FuShi.ViewModels.WeekGridViewModel"/>，视图不做任何业务计算。
/// <para>
/// 唯一的代码逻辑是「今日列车道」的定位：设计稿里它是一条从首行贯穿到末行的淡色竖带，
/// 但网格的列宽由 <c>*</c> 与真实窗口宽度共同决定，无法写死百分比，只能在布局完成后按真实列宽测量。
/// </para>
/// </summary>
public partial class WeekGridView : UserControl
{
    /// <summary>防止同一次布局内重复排队测量（Dispatcher 回调合并）。</summary>
    private bool _laneUpdateQueued;

    /// <summary>构造。</summary>
    public WeekGridView()
    {
        InitializeComponent();

        // 五条触发路径互补：首次显示、窗口改尺寸、页签切回、数据重载（DataContext 换新 VM）。
        // GridRoot 的尺寸变化单独接一条：ScrollViewer 视口变化时 UserControl 自身尺寸未必变，
        // 但列宽已经重排（例如最小化后还原），只靠 SizeChanged 会留下上一次量到的旧宽度。
        Loaded += (_, _) => ScheduleLaneUpdate();
        SizeChanged += (_, _) => ScheduleLaneUpdate();
        GridRoot.SizeChanged += (_, _) => ScheduleLaneUpdate();
        IsVisibleChanged += (_, _) => ScheduleLaneUpdate();
        DataContextChanged += (_, _) => ScheduleLaneUpdate();
    }

    /// <summary>把测量排到布局完成之后（Loaded 优先级），此时 ItemContainerGenerator 已生成容器。</summary>
    private void ScheduleLaneUpdate()
    {
        if (_laneUpdateQueued)
        {
            return;
        }

        _laneUpdateQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _laneUpdateQueued = false;
                UpdateTodayLane();
            }));
    }

    /// <summary>
    /// 量出「今天」那一列在首行的实际位置与宽度，以及首行顶到末行底的高度，写进车道 Border。
    /// 任一环节取不到容器（首次布局尚未完成、行数为 0）就隐藏车道，等下一次触发再算。
    /// </summary>
    private void UpdateTodayLane()
    {
        TodayLane.Visibility = Visibility.Collapsed;

        if (DataContext is not WeekGridViewModel vm || RowsHost.Items.Count == 0)
        {
            return;
        }

        if (!TryGetRowContainer(0, out var firstRow)
            || !TryGetRowContainer(RowsHost.Items.Count - 1, out var lastRow)
            || !TryGetCellContainer(firstRow, vm.TodayIndex, out var todayCell))
        {
            return;
        }

        try
        {
            // 横向取单元格（Button）而非 UniformGrid 单元：margin 已被排除，车道与染色层严格同宽。
            // 纵向取整行（含行内上下 padding），让车道像设计稿那样贯穿整条行带，而不是只包住格子。
            var cellLeft = todayCell.TransformToAncestor(GridRoot).Transform(new Point(0, 0)).X;
            var rowTop = firstRow.TransformToAncestor(GridRoot).Transform(new Point(0, 0)).Y;
            var lastBottom = lastRow.TransformToAncestor(GridRoot).Transform(new Point(0, lastRow.ActualHeight));
            var height = lastBottom.Y - rowTop;

            if (todayCell.ActualWidth <= 0 || height <= 0)
            {
                return;
            }

            TodayLane.Width = todayCell.ActualWidth;
            TodayLane.Height = height;
            TodayLane.Margin = new Thickness(cellLeft, rowTop, 0, 0);
            TodayLane.Visibility = Visibility.Visible;
        }
        catch (InvalidOperationException)
        {
            // 元素已从可视树摘下（窗口关闭 / 页签卸载）时 TransformToAncestor 会抛；忽略即可。
        }
    }

    /// <summary>取某一行模板的根元素（DataTemplate 里的 Grid）。</summary>
    private bool TryGetRowContainer(int index, out FrameworkElement rowContainer)
    {
        rowContainer = RowsHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement
                       ?? FindVisualChild<FrameworkElement>(RowsHost)!;
        return rowContainer is not null;
    }

    /// <summary>取某一行里第 <paramref name="index"/> 个单元格元素（Button）。</summary>
    private bool TryGetCellContainer(FrameworkElement rowContainer, int index, out FrameworkElement cell)
    {
        cell = null!;

        var cellsHost = FindVisualChild<ItemsControl>(rowContainer);
        if (cellsHost is null || index < 0 || index >= cellsHost.Items.Count)
        {
            return false;
        }

        if (cellsHost.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject presenter)
        {
            return false;
        }

        cell = (FindVisualChild<Button>(presenter) ?? presenter as FrameworkElement)!;
        return cell is not null;
    }

    /// <summary>深度优先找第一个指定类型的可视子元素（模板里的元素只能这样取）。</summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
