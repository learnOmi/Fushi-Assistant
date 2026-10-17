using System.Runtime.InteropServices;

namespace FuShi.Core;

internal static partial class Win32
{
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint pt);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }
}
