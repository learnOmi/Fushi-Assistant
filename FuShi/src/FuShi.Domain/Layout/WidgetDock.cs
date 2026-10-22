namespace FuShi.Domain.Layout;

/// <summary>吸附后的窗口左上角坐标（设备无关单位）。</summary>
/// <param name="Left">左边界。</param>
/// <param name="Top">上边界。</param>
public readonly record struct WidgetPosition(double Left, double Top);

/// <summary>
/// 本次吸附命中的边。<see cref="FlagsAttribute"/> 允许角落同时命中两条边，
/// 界面据此决定哪一侧角变直角、哪一条边亮高亮线。
/// </summary>
[Flags]
public enum DockEdges
{
    /// <summary>未吸附。</summary>
    None = 0,

    /// <summary>贴合工作区左边。</summary>
    Left = 1 << 0,

    /// <summary>贴合工作区右边。</summary>
    Right = 1 << 1,

    /// <summary>贴合工作区上边。</summary>
    Top = 1 << 2,

    /// <summary>贴合工作区下边。</summary>
    Bottom = 1 << 3,
}

/// <summary>吸附结果：吸附后的坐标 + 命中的边。</summary>
/// <param name="Left">吸附后的左边界。</param>
/// <param name="Top">吸附后的上边界。</param>
/// <param name="Edges">本次命中的边。</param>
public readonly record struct WidgetSnap(double Left, double Top, DockEdges Edges)
{
    /// <summary>只取坐标部分（不需要边的调用方可直接用）。</summary>
    public WidgetPosition Position => new(Left, Top);
}

/// <summary>
/// 悬浮窗贴边吸附的纯算法（M5b）。不含 WPF 依赖，可直接单测。
/// <para>
/// 规则：拖动结束后，窗口左/右边界距工作区对应边界 ≤ <see cref="SNAP_THRESHOLD"/> 时贴合；
/// 上下同理；水平与垂直各自独立判定。只做「靠近就贴」，不做越界夹取
/// （越界夹取是启动/恢复路径的职责，两者不要混在一起）。
/// </para>
/// </summary>
public static class WidgetDock
{
    /// <summary>
    /// 触发吸附的阈值（设备无关单位）。
    /// M5 调整：16 → 32。原阈值下命中后位移最多 16px，视觉上几乎看不出吸附；
    /// 32 DIP 在 150% 缩放下约等于 48 物理像素，快速拖动也更容易进入吸附区。
    /// </summary>
    public const double SNAP_THRESHOLD = 32.0;

    /// <summary>
    /// 计算吸附后的位置与命中的边。工作区与窗口坐标必须使用同一坐标系（调用方负责换算）。
    /// </summary>
    /// <param name="left">窗口左边界。</param>
    /// <param name="top">窗口上边界。</param>
    /// <param name="width">窗口宽度。</param>
    /// <param name="height">窗口高度。</param>
    /// <param name="workLeft">工作区左边界。</param>
    /// <param name="workTop">工作区上边界。</param>
    /// <param name="workRight">工作区右边界。</param>
    /// <param name="workBottom">工作区下边界。</param>
    /// <returns>吸附后的位置与命中的边；不满足阈值时原样返回坐标且 <see cref="DockEdges.None"/>。</returns>
    public static WidgetSnap Snap(
        double left,
        double top,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        var snappedLeft = left;
        var snappedTop = top;
        var edges = DockEdges.None;

        // 水平：先判左边界（窗口比工作区还宽时，左边界优先，避免右侧吸附把窗口推到屏幕外）。
        if (Math.Abs(left - workLeft) <= SNAP_THRESHOLD)
        {
            snappedLeft = workLeft;
            edges |= DockEdges.Left;
        }
        else if (Math.Abs(workRight - (left + width)) <= SNAP_THRESHOLD)
        {
            snappedLeft = workRight - width;
            edges |= DockEdges.Right;
        }

        if (Math.Abs(top - workTop) <= SNAP_THRESHOLD)
        {
            snappedTop = workTop;
            edges |= DockEdges.Top;
        }
        else if (Math.Abs(workBottom - (top + height)) <= SNAP_THRESHOLD)
        {
            snappedTop = workBottom - height;
            edges |= DockEdges.Bottom;
        }

        return new WidgetSnap(snappedLeft, snappedTop, edges);
    }
}
