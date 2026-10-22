using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FuShi.UI;

/// <summary>
/// 无边框窗口的平台适配（M3.5 §3.2）。
/// <para>
/// 承载 XAML 表达不了的两件事：Win11 的圆角外框，以及最大化时的边界修正。
/// 窗口拖拽区、缩放热区、玻璃帧都在 <c>WindowChrome</c> 上声明，不在这里重复配置。
/// </para>
/// </summary>
internal static class WindowChromeHelper
{
    /// <summary>DWM 窗口圆角偏好属性（Windows 11 起可用）。</summary>
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>圆角模式：系统默认圆角半径。</summary>
    private const int DWMWCP_ROUND = 2;

    /// <summary>DWM 深色标题栏属性（Windows 10 20H1 起可用）。</summary>
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>请求最大化 / 最小化尺寸限制的通知消息。</summary>
    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>取离窗口最近的显示器（多屏时避免算到主屏工作区）。</summary>
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>
    /// 单词窗口的适配入口：句柄就绪后挂消息钩子并请求圆角。
    /// </summary>
    /// <param name="window">已经过 <c>SourceInitialized</c> 的窗口。</param>
    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        TryApplyRoundedCorners(window);
    }

    /// <summary>
    /// 请求系统给窗口加圆角。
    /// </summary>
    /// <param name="window">已创建句柄的窗口（须在 <c>SourceInitialized</c> 之后调用）。</param>
    /// <returns>
    /// 成功返回 true；Win10 或远程会话下该属性不受支持，返回 false。
    /// 这是能力探测而非异常：Win10 退化为直角外框是设计上接受的降级（§3.2）。
    /// </returns>
    public static bool TryApplyRoundedCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var preference = DWMWCP_ROUND;
        return DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
    }

    /// <summary>
    /// 让系统标题栏跟随明暗主题（编辑对话框等保留原生标题栏的窗口用）。
    /// <para>
    /// 不处理时，暗色主题下会露出白色标题栏，与窗口内容形成硬边界；
    /// 该属性由 DWM 绘制，XAML 无法表达，只能在句柄就绪后请求一次。
    /// </para>
    /// </summary>
    /// <param name="window">已创建句柄的窗口（须在 <c>SourceInitialized</c> 之后调用）。</param>
    /// <param name="useDark">true = 深色标题栏；false = 浅色标题栏。</param>
    /// <returns>成功返回 true；系统不支持该属性时返回 false（保持默认外观，属可接受降级）。</returns>
    public static bool TryApplyCaptionTheme(Window window, bool useDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var enabled = useDark ? 1 : 0;
        return DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) == 0;
    }

    /// <summary>
    /// 取窗口当前所在显示器的工作区，并换算到「与 <c>Window.Left/Top</c> 同一坐标系」（M5b 贴边吸附用）。
    /// <para>
    /// 不直接除 DPI 做绝对换算：物理坐标原点与 WPF 逻辑坐标原点的关系在混合 DPI 多屏下并不直观。
    /// 这里用「工作区与窗口物理矩形的差值 ÷ 当前显示器缩放」得到边界相对窗口的偏移，再加回
    /// <c>Left/Top</c>，因此跨屏（含负坐标）与不同缩放都能对齐。
    /// </para>
    /// </summary>
    /// <param name="window">已创建句柄的窗口。</param>
    /// <returns>工作区矩形（设备无关单位，与窗口位置同坐标系）；句柄未就绪时退回主屏工作区。</returns>
    public static Rect GetWorkAreaInWindowCoordinates(Window window)
    {
        var fallback = SystemParameters.WorkArea;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return fallback;
        }

        var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return fallback;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var left = window.Left + (info.rcWork.Left - rect.Left) / dpi.DpiScaleX;
        var top = window.Top + (info.rcWork.Top - rect.Top) / dpi.DpiScaleY;
        var right = window.Left + window.ActualWidth + (info.rcWork.Right - rect.Right) / dpi.DpiScaleX;
        var bottom = window.Top + window.ActualHeight + (info.rcWork.Bottom - rect.Bottom) / dpi.DpiScaleY;

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 最大化边界修正（§7 风险 2）：WPF 的 <c>WindowChrome</c> 在 150% 缩放下会把最大化窗口
    /// 放大到「整块屏幕 + 缩放边框」，底部压到任务栏下面。这里直接用显示器工作区覆盖。
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            var limits = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            limits.ptMaxPosition.X = info.rcWork.Left;
            limits.ptMaxPosition.Y = info.rcWork.Top;
            limits.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
            limits.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
            Marshal.StructureToPtr(limits, lParam, false);
        }

        // 拦下消息：若放行，WindowChrome 的窗口过程会用自己的非客户区补偿值再算一遍，
        // 在 150% 缩放下把宽度放大到 屏幕 + 边框（实测 3860×2180）。
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
