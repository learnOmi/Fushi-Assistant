using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FuShi.Core;
using FuShi.UI;

namespace FuShi;

public partial class App : Application
{
    private const string MutexId = "FuShi.SingleInstance.Mutex";

    private Mutex? _mutex;
    private WidgetWindow? _widget;
    private WidgetStateStore _store = null!;
    private System.Windows.Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：二次启动直接退出
        _mutex = new Mutex(true, MutexId, out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _store = WidgetStateStore.Load();

        _widget = new WidgetWindow(_store);
        PositionWidget();
        _widget.Show();

        CreateTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _widget?.SavePosition();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    // ---------- 悬浮窗位置：首次出现在主屏右上角，之后记住用户拖放的位置 ----------
    private void PositionWidget()
    {
        if (_widget is null) return;
        if (_store.HasSavedPosition)
        {
            _widget.Left = _store.X;
            _widget.Top = _store.Y;
            return;
        }
        var area = SystemParameters.WorkArea;
        _widget.Left = area.Right - _widget.ActualWidth - 24;
        _widget.Top = area.Top + 24;
    }

    // ---------- 托盘常驻 ----------
    private void CreateTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "浮时 · 课表提醒",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏悬浮窗", null, (_, _) => ToggleWidget());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ToggleWidget();
    }

    private void ToggleWidget()
    {
        if (_widget is null) return;
        if (_widget.IsVisible) _widget.Hide();
        else
        {
            _widget.Show();
            _widget.Activate();
        }
    }

    /// <summary>运行时绘制托盘图标：黑团子剪影（近黑圆角团 + 两只发光眼）。</summary>
    private static System.Drawing.Icon MakeTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var body = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(30, 30, 36));
        using var eye = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(207, 233, 255));
        g.FillEllipse(body, 4, 5, 24, 22);
        g.FillEllipse(eye, 11, 12, 3, 6);
        g.FillEllipse(eye, 18, 12, 3, 6);

        var hIcon = bmp.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(hIcon);
        // 复制一份，原始句柄用完即毁
        var clone = (System.Drawing.Icon)icon.Clone();
        Win32.DestroyIcon(hIcon);
        icon.Dispose();
        return clone;
    }
}
