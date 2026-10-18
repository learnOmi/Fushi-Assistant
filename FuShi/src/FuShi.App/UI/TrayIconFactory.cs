using System.Drawing;
using System.Drawing.Drawing2D;
using FuShi.Core;

namespace FuShi.UI;

/// <summary>
/// 托盘图标工厂：运行时用 GDI+ 绘制「黑团子剪影」（近黑圆角团 + 两只发光眼）。
/// 保留 M1 的绘制方式而非改用 .ico 资源，是为了零额外资源文件即得品牌形象。
/// </summary>
internal static class TrayIconFactory
{
    /// <summary>图标位图边长（像素）。托盘图标 16/32 通用。</summary>
    private const int ICON_SIZE_PX = 32;

    /// <summary>团身椭圆的边界（左, 上, 宽, 高）。</summary>
    private const int BODY_X = 4;
    private const int BODY_Y = 5;
    private const int BODY_W = 24;
    private const int BODY_H = 22;

    /// <summary>左眼椭圆的边界与尺寸。</summary>
    private const int EYE_LEFT_X = 11;
    private const int EYE_Y = 12;
    private const int EYE_W = 3;
    private const int EYE_H = 6;

    /// <summary>右眼椭圆的横向起点。</summary>
    private const int EYE_RIGHT_X = 18;

    /// <summary>绘制并返回托盘图标。调用方负责在退出时释放。</summary>
    public static Icon Create()
    {
        using var bitmap = new Bitmap(ICON_SIZE_PX, ICON_SIZE_PX);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var bodyBrush = new SolidBrush(Color.FromArgb(30, 30, 36));
        using var eyeBrush = new SolidBrush(Color.FromArgb(207, 233, 255));

        graphics.FillEllipse(bodyBrush, BODY_X, BODY_Y, BODY_W, BODY_H);
        graphics.FillEllipse(eyeBrush, EYE_LEFT_X, EYE_Y, EYE_W, EYE_H);
        graphics.FillEllipse(eyeBrush, EYE_RIGHT_X, EYE_Y, EYE_W, EYE_H);

        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);

        // 复制一份，原始句柄用完即毁（GDI 句柄必须显式释放，否则泄漏）。
        var clone = (Icon)icon.Clone();
        Win32.DestroyIcon(handle);
        icon.Dispose();
        return clone;
    }
}
