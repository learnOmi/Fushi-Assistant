using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Time;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using FuShi.Notifications;
using FuShi.UI;
using FuShi.UI.Dialogs;
using FuShi.ViewModels;
using H.NotifyIcon;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;

namespace FuShi;

/// <summary>
/// 应用入口：单实例守护、DI 宿主装配、数据库迁移、托盘与悬浮窗的创建，以及 M2b 的提醒引擎启动。
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
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainWindowViewModel;
    private TaskbarIcon? _tray;
    private Serilog.ILogger _logger = null!;

    private ScheduleService? _scheduleService;
    private ToastNotificationChannel? _toastChannel;
    private NotificationService? _notifications;
    private ReminderEngine? _engine;

    /// <summary>悬浮组件数据兜底重载定时器（跨天滚动与引擎事件之外的安全网）。</summary>
    private DispatcherTimer? _widgetReloadTimer;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // 日志最先装配：OnExit 无条件引用 _logger，而二次实例会在 base.OnStartup 之前
        // 就走到 Shutdown() 分支；若此时 _logger 仍为 null，OnExit 会抛 NullReferenceException。
        _logger = LoggingSetup.Create();

        // 单实例：二次启动直接退出（避免两个引擎/两套托盘）。
        _mutex = new Mutex(true, MUTEX_ID, out var isNewInstance);
        if (!isNewInstance)
        {
            _logger.Information("已有实例在运行，二次启动退出");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _logger.Information("浮时启动");

        _host = BuildHost();
        InitializeDatabase();

        var settingsRepository = _host.Services.GetRequiredService<AppSettingRepository>();
        var settings = settingsRepository.GetAsync().GetAwaiter().GetResult();
        LogTimeZoneResolution(settings.TimezoneId);

        CreateTray();
        CreateWidget(settingsRepository, settings);
        StartEngine();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // 收尾顺序：先摘掉外部事件与定时器，再停引擎（它会取消后台循环），最后释放宿主。
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _widgetReloadTimer?.Stop();
        if (_widgetReloadTimer is not null)
        {
            _widgetReloadTimer.Tick -= OnWidgetReloadTick;
        }

        if (_notifications is not null)
        {
            _notifications.RescanRequested -= OnRescanRequested;
        }

        if (_engine is not null)
        {
            _engine.ScheduleChanged -= OnScheduleChanged;
            _engine.Dispose();
        }

        _widget?.SavePosition();
        if (_widget is not null)
        {
            _widget.OpenScheduleRequested -= OnOpenScheduleRequested;
        }

        _tray?.Dispose();
        _host?.Dispose();

        _logger.Information("浮时退出");
        (_logger as IDisposable)?.Dispose(); // Serilog 的具体实现实现了 IDisposable，接口未暴露，故显式转换。

        _mutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// 构建 DI 宿主。存储用 <see cref="EntityFrameworkServiceCollectionExtensions.AddDbContextFactory{TContext}"/>
    /// 注册工厂——每次操作创建短生命周期上下文，隔离 UI 线程与引擎后台线程。
    /// M2b 追加：课表服务、Toast 通道、通知调度器、提醒引擎。
    /// </summary>
    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContextFactory<FuShiDbContext>(
            options => options.UseSqlite(StoragePaths.ConnectionString));
        builder.Services.AddSingleton<Serilog.ILogger>(_logger);
        builder.Services.AddSingleton<AppSettingRepository>();

        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<ScheduleEditService>();
        builder.Services.AddSingleton<ReminderResponseService>();
        builder.Services.AddSingleton<ToastNotificationChannel>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<INotificationChannel>(provider => provider.GetRequiredService<NotificationService>());
        builder.Services.AddSingleton<ReminderEngine>();

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
        _tray.DataContext = new TrayViewModel(ToggleWidget, OpenMainWindow, ExitApplication);
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
        _widget.OpenScheduleRequested += OnOpenScheduleRequested;
        PositionWidget(viewModel);
        ApplyWidgetOpacity(settings.Opacity); // 按设置回放不透明度，避免启动瞬间闪一下全不透明
        _widget.Show();
    }

    /// <summary>
    /// 启动提醒引擎并接好两侧的「线」：
    /// 引擎的数据变更事件 → 刷新悬浮组件；系统唤醒事件 → 只请求重排；兜底定时器 → 周期重载。
    /// </summary>
    private void StartEngine()
    {
        _scheduleService = _host!.Services.GetRequiredService<ScheduleService>();

        // 风险 6 的验证点：先探测非打包进程能否弹 Toast，结论写入日志（失败则自动降级）。
        _toastChannel = _host.Services.GetRequiredService<ToastNotificationChannel>();
        _toastChannel.Probe();

        _engine = _host.Services.GetRequiredService<ReminderEngine>();
        _engine.ScheduleChanged += OnScheduleChanged;

        // 弹窗动作（稍后 / 开始）会改变未来触发点：接到通知后唤醒引擎重算休眠。
        _notifications = _host.Services.GetRequiredService<NotificationService>();
        _notifications.RescanRequested += OnRescanRequested;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _widgetReloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UiConstants.WIDGET_DATA_RELOAD_INTERVAL_MS),
        };
        _widgetReloadTimer.Tick += OnWidgetReloadTick;
        _widgetReloadTimer.Start();

        ReloadWidget(); // 先铺一次数据再让引擎跑起来，避免首屏空窗。
        _engine.Start();
    }

    /// <summary>引擎报告「课表数据可能已变」→ 编组回 UI 线程重载悬浮组件。</summary>
    private void OnScheduleChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(ReloadWidget));
    }

    /// <summary>兜底重载定时器到点。</summary>
    private void OnWidgetReloadTick(object? sender, EventArgs e) => ReloadWidget();

    /// <summary>
    /// 弹窗动作落库后请求引擎重排休眠（见 <see cref="NotificationService.RescanRequested"/>）；
    /// 同时立即刷新悬浮组件，使「已稍后 / 已跳过」的反馈在点完按钮后即可见（无需等兜底定时器）。
    /// 该事件在 UI 线程（弹窗回调）上触发，故可直接调用同步取数的 <see cref="ReloadWidget"/>。
    /// </summary>
    private void OnRescanRequested(object? sender, EventArgs e)
    {
        _engine?.RequestRescan();
        ReloadWidget();

        // 「稍后」是实例级顺延、只改「今天」，不经过课表编辑命令：若主窗口已创建，顺带刷新其周网格，
        // 使今天那一列的时段与「已顺延」角标即时更新（M3c）。异步刷新，不阻塞弹窗回调。
        _ = _mainWindowViewModel?.RefreshGridAsync();
    }

    /// <summary>
    /// 系统电源模式变化。<b>该处理器跑在系统隐藏窗口的消息泵线程上</b>，
    /// 因此只允许调用引擎的「取消休眠」这一个动作，绝不触碰 UI 或 DbContext。
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _engine?.RequestRescan();
        }
    }

    /// <summary>把当前课表时间线推给悬浮组件（在 UI 线程上同步取数后转交视图）。</summary>
    private void ReloadWidget()
    {
        if (_widget is null || _scheduleService is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        // 与 ScheduleService 的展开视野保持一致的 7 天窗口（双闭区间，故 -1）。
        var horizonEnd = today.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var slots = _scheduleService
            .GetTimelineAsync(today, horizonEnd)
            .GetAwaiter()
            .GetResult();

        _widget.ApplySchedule(slots);
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

    /// <summary>退出应用：先真正关闭主窗口（绕过"关闭即隐藏"），再请求 WPF 关闭。</summary>
    private void ExitApplication()
    {
        _mainWindow?.ForceClose();
        Shutdown();
    }

    /// <summary>
    /// 打开 / 唤起主窗口（托盘左键、悬浮组件双击与「打开课表」按钮共用）。
    /// 主窗口惰性创建、单例持有；关闭只是隐藏（见 <see cref="MainWindow"/>）。
    /// </summary>
    private void OpenMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = CreateMainWindow();
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    /// <summary>悬浮组件请求打开课表。</summary>
    private void OnOpenScheduleRequested(object? sender, EventArgs e) => OpenMainWindow();

    /// <summary>按需装配主窗口：注入写/读服务、引擎、悬浮组件刷新回调与对话框委托。</summary>
    private MainWindow CreateMainWindow()
    {
        var viewModel = new MainWindowViewModel(
            _host!.Services.GetRequiredService<ScheduleEditService>(),
            _host.Services.GetRequiredService<ScheduleService>(),
            _host.Services.GetRequiredService<ReminderEngine>(),
            CreateSettingsViewModel(),
            ReloadWidget,
            ShowEntryEditor,
            ShowSubjectEditor,
            _logger);

        _mainWindowViewModel = viewModel;
        return new MainWindow(viewModel);
    }

    /// <summary>装配设置页视图模型：注入仓储、引擎，以及本宿主的两个即时生效回调。</summary>
    private SettingsViewModel CreateSettingsViewModel()
    {
        return new SettingsViewModel(
            _host!.Services.GetRequiredService<AppSettingRepository>(),
            _host.Services.GetRequiredService<ReminderEngine>(),
            ReloadWidget,
            ApplyWidgetOpacity,
            _logger);
    }

    /// <summary>把不透明度应用到悬浮窗（设置页实时预览与启动回放共用）。</summary>
    /// <param name="opacity">不透明度（0.3~1.0）。</param>
    private void ApplyWidgetOpacity(double opacity)
    {
        if (_widget is not null)
        {
            _widget.Opacity = opacity;
        }
    }

    /// <summary>弹出时段编辑对话框，返回用户是否点「确定」。</summary>
    private bool ShowEntryEditor(EntryEditorViewModel viewModel)
    {
        var dialog = new EntryEditorDialog(viewModel) { Owner = ActiveOwnerWindow() };
        return dialog.ShowDialog() == true;
    }

    /// <summary>弹出新建科目对话框，返回用户是否点「确定」。</summary>
    private bool ShowSubjectEditor(SubjectEditorViewModel viewModel)
    {
        var dialog = new SubjectEditorDialog(viewModel) { Owner = ActiveOwnerWindow() };
        return dialog.ShowDialog() == true;
    }

    /// <summary>取当前活动窗口作为模态对话框的 Owner；若无则退回主窗口。</summary>
    private Window? ActiveOwnerWindow()
    {
        var active = Windows
            .OfType<Window>()
            .FirstOrDefault(static window => window.IsActive);
        return active ?? _mainWindow;
    }
}
