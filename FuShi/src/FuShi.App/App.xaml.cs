using System.Threading;
using System.Windows;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Domain.Time;
using FuShi.Infrastructure;
using FuShi.UI;
using FuShi.ViewModels;
using H.NotifyIcon;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FuShi;

/// <summary>
/// 应用入口：单实例守护、DI 宿主装配、数据库迁移、托盘与悬浮窗的创建。
/// M2 起改为 MVVM + DI：所有服务与状态经容器装配，窗口只保留纯视图职责。
/// </summary>
public partial class App : Application
{
    /// <summary>单实例互斥体名。</summary>
    private const string MUTEX_ID = "FuShi.SingleInstance.Mutex";

    /// <summary>App.xaml 中托盘控件的资源键。</summary>
    private const string TRAY_ICON_RESOURCE_KEY = "TrayIcon";

    /// <summary>悬浮窗首次出现时距屏幕边缘的留白（设备无关单位）。</summary>
    private const double DEFAULT_WIDGET_MARGIN = 24.0;

    private Mutex? _mutex;
    private IHost? _host;
    private WidgetWindow? _widget;
    private TaskbarIcon? _tray;
    private Serilog.ILogger _logger = null!;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：二次启动直接退出（避免两个引擎/两套托盘）。
        _mutex = new Mutex(true, MUTEX_ID, out var isNewInstance);
        if (!isNewInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _logger = LoggingSetup.Create();
        _logger.Information("浮时启动");

        _host = BuildHost();
        InitializeDatabase();

        var settingsRepository = _host.Services.GetRequiredService<AppSettingRepository>();
        var settings = settingsRepository.GetAsync().GetAwaiter().GetResult();
        LogTimeZoneResolution(settings.TimezoneId);

        CreateTray();
        CreateWidget(settingsRepository, settings);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _widget?.SavePosition();
        _tray?.Dispose();
        _host?.Dispose();

        _logger.Information("浮时退出");
        (_logger as IDisposable)?.Dispose(); // Serilog 的具体实现实现了 IDisposable，接口未暴露，故显式转换。

        _mutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// 构建 DI 宿主。存储用 <see cref="EntityFrameworkServiceCollectionExtensions.AddDbContextFactory{TContext}"/>
    /// 注册工厂——每次操作创建短生命周期上下文，隔离 UI 线程与（M2b 的）引擎后台线程。
    /// </summary>
    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContextFactory<FuShiDbContext>(
            options => options.UseSqlite(StoragePaths.ConnectionString));
        builder.Services.AddSingleton<Serilog.ILogger>(_logger);
        builder.Services.AddSingleton<AppSettingRepository>();

        return builder.Build();
    }

    /// <summary>建库 / 升级到最新迁移；损坏时自动重建。</summary>
    private void InitializeDatabase()
    {
        var factory = _host!.Services.GetRequiredService<IDbContextFactory<FuShiDbContext>>();
        DatabaseInitializer.MigrateOrRebuild(factory, _logger);
    }

    /// <summary>解析时区；若走了兜底，记一条警告，不静默降级。</summary>
    /// <param name="timezoneId">配置中的时区 ID。</param>
    private void LogTimeZoneResolution(string timezoneId)
    {
        var lookup = TimeZoneResolver.Resolve(timezoneId);
        if (lookup.UsedFallback)
        {
            _logger.Warning("时区 {TimezoneId} 未被直接识别，已回退到 {ResolvedId}", timezoneId, lookup.Zone.Id);
        }
    }

    /// <summary>从 App.xaml 取托盘控件，注入命令与图标，并强制创建（资源中声明时需显式创建）。</summary>
    private void CreateTray()
    {
        _tray = (TaskbarIcon)FindResource(TRAY_ICON_RESOURCE_KEY);
        _tray.DataContext = new TrayViewModel(ToggleWidget, Shutdown);
        _tray.Icon = TrayIconFactory.Create();
        _tray.ToolTipText = UiConstants.TRAY_TOOLTIP_TEXT;

        // 关闭 Efficiency Mode 以保持与 M1 相同的调度行为（不退步）；后台省电在 M2b 引擎里单独评估。
        _tray.ForceCreate(false);
    }

    /// <summary>创建悬浮窗并绑定视图模型，恢复上次位置或按默认右上角摆放。</summary>
    /// <param name="settingsRepository">设置仓储。</param>
    /// <param name="settings">启动时加载的设置快照。</param>
    private void CreateWidget(AppSettingRepository settingsRepository, AppSetting settings)
    {
        var viewModel = new WidgetViewModel(settingsRepository, settings);
        _widget = new WidgetWindow(viewModel);
        PositionWidget(viewModel);
        _widget.Show();
    }

    /// <summary>恢复悬浮窗位置：有保存值则用之，否则落到主屏右上角。</summary>
    /// <param name="viewModel">悬浮窗视图模型（提供已保存位置）。</param>
    private void PositionWidget(WidgetViewModel viewModel)
    {
        if (_widget is null)
        {
            return;
        }

        if (viewModel.HasSavedPosition)
        {
            _widget.Left = viewModel.WidgetX;
            _widget.Top = viewModel.WidgetY;
            return;
        }

        var area = SystemParameters.WorkArea;
        _widget.Left = area.Right - _widget.ActualWidth - DEFAULT_WIDGET_MARGIN;
        _widget.Top = area.Top + DEFAULT_WIDGET_MARGIN;
    }

    /// <summary>显示 / 隐藏悬浮窗。仅从 UI 线程调用（托盘命令在 UI 线程执行）。</summary>
    private void ToggleWidget()
    {
        if (_widget is null)
        {
            return;
        }

        if (_widget.IsVisible)
        {
            _widget.Hide();
        }
        else
        {
            _widget.Show();
            _widget.Activate();
        }
    }
}
