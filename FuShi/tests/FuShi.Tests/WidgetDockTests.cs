using FluentAssertions;
using FuShi.Domain.Layout;

namespace FuShi.Tests;

/// <summary>M5b：悬浮窗贴边吸附纯函数测试（四方向 / 阈值内外 / 跨屏负坐标）。</summary>
public sealed class WidgetDockTests
{
    private const double WIDTH = 260;
    private const double HEIGHT = 120;

    /// <summary>左边界在阈值内：贴合工作区左边。</summary>
    [Fact]
    public void Snap_LeftWithinThreshold_DocksToLeft()
    {
        var result = WidgetDock.Snap(8, 300, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Left.Should().Be(0);
        result.Top.Should().Be(300, "垂直方向不受影响");
        result.Edges.Should().Be(DockEdges.Left);
    }

    /// <summary>右边界在阈值内：贴合工作区右边。</summary>
    [Fact]
    public void Snap_RightWithinThreshold_DocksToRight()
    {
        var left = 1920 - WIDTH - 10;
        var result = WidgetDock.Snap(left, 300, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Left.Should().Be(1920 - WIDTH);
        result.Edges.Should().Be(DockEdges.Right);
    }

    /// <summary>上边界在阈值内：贴合工作区顶部。</summary>
    [Fact]
    public void Snap_TopWithinThreshold_DocksToTop()
    {
        var result = WidgetDock.Snap(500, 5, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Top.Should().Be(0);
        result.Edges.Should().Be(DockEdges.Top);
    }

    /// <summary>下边界在阈值内：贴合工作区底部。</summary>
    [Fact]
    public void Snap_BottomWithinThreshold_DocksToBottom()
    {
        var top = 1040 - HEIGHT - 12;
        var result = WidgetDock.Snap(500, top, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Top.Should().Be(1040 - HEIGHT);
        result.Edges.Should().Be(DockEdges.Bottom);
    }

    /// <summary>四边都远离阈值：位置保持不变。</summary>
    [Fact]
    public void Snap_OutsideThreshold_KeepsPosition()
    {
        var result = WidgetDock.Snap(400, 300, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Left.Should().Be(400);
        result.Top.Should().Be(300);
        result.Edges.Should().Be(DockEdges.None);
    }

    /// <summary>贴角落时两条边同时命中（界面据此把两个角变直角、亮两条高亮线）。</summary>
    [Fact]
    public void Snap_AtCorner_ReportsBothEdges()
    {
        var result = WidgetDock.Snap(6, 8, WIDTH, HEIGHT, 0, 0, 1920, 1040);
        result.Left.Should().Be(0);
        result.Top.Should().Be(0);
        result.Edges.Should().Be(DockEdges.Left | DockEdges.Top);
    }

    /// <summary>恰好等于阈值仍吸附；超过 1 像素则不吸附（边界判定）。</summary>
    [Fact]
    public void Snap_ExactlyAtThreshold_Docks_ButBeyondDoesNot()
    {
        WidgetDock.Snap(WidgetDock.SNAP_THRESHOLD, 300, WIDTH, HEIGHT, 0, 0, 1920, 1040)
            .Left.Should().Be(0);
        WidgetDock.Snap(WidgetDock.SNAP_THRESHOLD + 1, 300, WIDTH, HEIGHT, 0, 0, 1920, 1040)
            .Left.Should().Be(WidgetDock.SNAP_THRESHOLD + 1);
    }

    /// <summary>副屏位于主屏左侧（工作区为负坐标）时仍能正确贴合右边界。</summary>
    [Fact]
    public void Snap_SecondaryScreenWithNegativeOrigin_DocksToRight()
    {
        var left = -WIDTH - 6; // 副屏工作区 [-1920, 0]
        var result = WidgetDock.Snap(left, 100, WIDTH, HEIGHT, -1920, 0, 0, 1040);
        result.Left.Should().Be(-WIDTH);
    }

    /// <summary>左右两侧同时进入阈值（窗口几乎占满工作区）时左边界优先，行为可预期。</summary>
    [Fact]
    public void Snap_BothHorizontalEdgesWithinThreshold_PrefersLeftEdge()
    {
        // 左距 8、右距 12，两侧都该贴；约定左侧优先。
        var result = WidgetDock.Snap(8, 300, 1900, HEIGHT, 0, 0, 1920, 1040);
        result.Left.Should().Be(0);
    }
}
