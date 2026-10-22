using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Time;
using FuShi.Infrastructure;
using FuShi.Infrastructure.Scheduling;
using FuShi.Infrastructure.Statistics;
using FuShi.Infrastructure.Tasks;
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

    /// <summary>
    /// 结束当前线程的「活动菜单」（Win32 原生弹出菜单）。托盘右键菜单若由 H.NotifyIcon 以原生方式弹出，
    /// WPF 的 <c>ContextMenu.IsOpen</c> 管不到它；退出时补一刀 <c>EndMenu</c> 兜底。无活动菜单时返回 0，属无副作用调用。
    /// </summary>
    /// <returns>成功结束菜单返回非零；当前线程无活动菜单返回 0。</returns>
    [DllImport("user32.dll")]
    private static extern bool EndMenu();

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

    /// <summary>主题服务：启动时应用一次，设置页切换主题时再次调用。</summary>
    private ThemeService? _themeService;

    /// <summary>悬浮组件数据兜底重载定时器（跨天滚动与引擎事件之外的安全网）。</summary>
    private DispatcherTimer? _widgetReloadTimer;

    /// <summary>退出流程是否已启动：防止重复点击「退出」或重入收尾。</summary>
    private bool _exiting;

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

        // 主题必须早于任何窗口创建：托盘图标、悬浮窗与主窗口都会在加载时解析令牌，
        // 晚于窗口创建会让首帧先按旧主题渲染再闪一下。
        _themeService = new ThemeService(this, _logger);
#if DEBUG
        _themeService.VerifyKeyParity();
#endif
        _themeService.Apply(settings.Theme);

        CreateTray();
        CreateWidget(settingsRepository, settings);
        StartEngine();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 收尾顺序：先摘掉外部事件与定时器，再停引擎（它会取消后台循环），最后释放宿主。
    /// <para>
    /// <b>每一步都独立兜底并计耗时</b>：本方法跑在 UI 线程上，任何一步抛出（例如宿主二次释放
    /// DI 单例）都会让后续步骤、乃至进程真正退出所依赖的 <c>base.OnExit</c> 被跳过，
    /// 表现为「进程残留」。故此处逐步 try/catch，既保证收尾不中断，也让「退出卡在哪一段」可查
    /// ——设计文档运行期缺陷 4 的排查手段。
    /// </para>
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        var exitWatch = Stopwatch.StartNew();

        RunExitStep(() => SystemEvents.PowerModeChanged -= OnPowerModeChanged, "摘除电源事件", exitWatch);
        RunExitStep(StopWidgetReloadTimer, "停止兜底定时器", exitWatch);
        RunExitStep(UnhookRescanRequested, "摘除通知重排事件", exitWatch);
        RunExitStep(StopReminderEngine, "停止提醒引擎", exitWatch);
        RunExitStep(() => _widget?.SavePosition(), "保存悬浮窗位置", exitWatch);
        RunExitStep(UnhookOpenScheduleRequested, "摘除悬浮窗事件", exitWatch);
        RunExitStep(() => _tray?.Dispose(), "释放托盘图标", exitWatch);

        // 释放 DI 宿主会把日志器一并关掉（ILogger 以<b>实例</b>方式注册进容器，容器退出时会 Dispose 它），
        // 自那时起再写日志会被 Serilog 静默丢弃——上一轮排查中「浮时退出」凭空消失就是这个原因
        // （不是退出流程被异常打断）。因此必须<b>先把累计耗时落盘</b>，再做这最后一步。
        _logger.Information("浮时退出：收尾累计 {Ms}ms（以下仅剩释放宿主，它之后日志器即关闭）", exitWatch.ElapsedMilliseconds);

        // 释放宿主：容器会反向释放各单例（提醒引擎等），单步失败已由 RunExitStep 兜住，
        // 保证后续的 base.OnExit 一定会执行——否则 Dispatcher 不会关停，进程会残留。
        RunExitStep(() => _host?.Dispose(), "释放 DI 宿主", exitWatch);

        // 此后日志器已关闭：不得再写日志，只做无声兜底。
        DisposeLoggerQuietly();
        DisposeMutexQuietly();

        base.OnExit(e);
    }

    /// <summary>执行一个退出收尾步骤；任一步失败只记录、不中断后续步骤与进程退出。</summary>
    /// <param name="step">收尾动作。</param>
    /// <param name="name">步骤名（写入日志，便于分段定位卡顿点）。</param>
    /// <param name="watch">退出总计时器，用于算出本段耗时。</param>
    private void RunExitStep(Action step, string name, Stopwatch watch)
    {
        var before = watch.ElapsedMilliseconds;

        try
        {
            step();
        }
        catch (Exception ex)
        {
            // 退出路径兜底：单步失败不能阻止进程退出，但绝不静默——记错误后继续下一步。
            _logger.Error(ex, "退出收尾步骤「{Step}」失败，已跳过", name);
        }

        _logger.Information("退出收尾：{Step} 用时 {Ms}ms", name, watch.ElapsedMilliseconds - before);
    }

    /// <summary>停止悬浮组件的兜底重载定时器并摘除其 Tick。</summary>
    private void StopWidgetReloadTimer()
    {
        _widgetReloadTimer?.Stop();
        if (_widgetReloadTimer is not null)
        {
            _widgetReloadTimer.Tick -= OnWidgetReloadTick;
        }
    }

    /// <summary>摘除通知服务的重排事件（引擎即将停止，不再需要它唤醒）。</summary>
    private void UnhookRescanRequested()
    {
        if (_notifications is not null)
        {
            _notifications.RescanRequested -= OnRescanRequested;
        }
    }

    /// <summary>停止提醒引擎并摘除其「数据变更」事件。</summary>
    private void StopReminderEngine()
    {
        if (_engine is not null)
        {
            _engine.ScheduleChanged -= OnScheduleChanged;
            _engine.Dispose();
        }
    }

    /// <summary>摘除悬浮组件的「打开课表」事件。</summary>
    private void UnhookOpenScheduleRequested()
    {
        if (_widget is not null)
        {
            _widget.OpenScheduleRequested -= OnOpenScheduleRequested;
        }
    }

    /// <summary>
    /// 关闭日志器。此步在宿主释放之后执行：若宿主已代为释放（日志器以实例方式注册进容器，
    /// 容器退出时会 Dispose 它），这里是幂等空操作；若宿主未接管，这一步保证日志缓冲被刷盘。
    /// 失败无处可记——毕竟要关的就是日志器本身；此时进程即将结束，无用户可感知影响。
    /// </summary>
    private void DisposeLoggerQuietly()
    {
        try
        {
            (_logger as IDisposable)?.Dispose(); // Serilog 的具体实现实现了 IDisposable，接口未暴露，故显式转换。
        }
        catch (Exception)
        {
            // 日志器关闭失败：无可写之处，且进程即将结束。
        }
    }

    /// <summary>
    /// 释放单实例互斥体。此步在日志器关闭之后执行，故失败时无处可记——只能就地吞掉，
    /// 且此时进程即将结束，互斥体的释放失败对用户没有任何可感知影响。
    /// </summary>
    private void DisposeMutexQuietly()
    {
        try
        {
            _mutex?.Dispose();
        }
        catch (Exception)
        {
            // 日志器已关闭，且进程正在退出：无需也无法上报。
        }
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
        builder.Services.AddSingleton<PhaseService>();
        builder.Services.AddSingleton<ReminderResponseService>();
        builder.Services.AddSingleton<StatisticsService>();
        builder.Services.AddSingleton<TaskService>();
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
        _tray.DataContext = new TrayViewModel(ToggleWidget, OpenMainWindow, RequestExit);
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

    /// <summary>
    /// 托盘「退出」的入口。
    /// <para>
    /// 菜单项命令是在 Popup <b>仍打开时同步执行</b>的：若在这里直接做收尾，UI 线程会被占住，
    /// Popup 的关闭与渲染来不及跑完，菜单就「赖」在屏幕上几秒（运行期缺陷 4 的观感来源）。
    /// 因此命令本身只排一次 <see cref="DispatcherPriority.ApplicationIdle"/> 回调就返回——
    /// 排到这个优先级时队列已空（菜单已关闭并渲染完毕），再执行真正的收尾。
    /// </para>
    /// </summary>
    private void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        _logger.Information("托盘退出：收到退出请求，待调度器空闲后执行收尾");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ExitApplication));
    }

    /// <summary>
    /// 退出应用：先收起菜单、再<b>释放托盘图标本身</b>（这一步才是真正带走菜单的关键），最后关窗与退出。
    /// 全程记录分段耗时，配合 <see cref="OnExit"/> 的收尾日志构成完整的退出时延证据链。
    /// </summary>
    private void ExitApplication()
    {
        // 重入保护：ApplicationIdle 回调期间可能被再次触发（重复点击、或另一条退出入口）。
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        var exitWatch = Stopwatch.StartNew();
        _logger.Information("托盘退出：开始");

        // 第 1 步：收起托盘右键菜单。两条路一起上，覆盖「WPF ContextMenu」与「原生菜单」两种可能：
        // ① 置 ContextMenu.IsOpen=false（WPF 路径）；
        // ② EndMenu() 结束当前线程的活动菜单（Win32 原生路径，无菜单时是空操作）。
        CloseTrayMenu();
        EndMenu();

        // 第 2 步：释放托盘图标。TaskbarIcon.Dispose() 会拆掉它自持的隐藏窗口，把挂在这条链路上的
        // 菜单一并带走。必须放在 Shutdown 之前，且只做一次（随后置空，避免 OnExit 重复释放）。
        if (_tray is not null)
        {
            try
            {
                _tray.Dispose();
            }
            catch (Exception ex)
            {
                // 退出路径的兜底：释放失败也不能阻止应用退出；记录即可（绝不静默吞掉异常）。
                _logger.Warning(ex, "释放托盘图标失败");
            }

            // 置空：使 OnExit 的 _tray?.Dispose() 成为空操作，避免重复释放。
            _tray = null;
        }

        _logger.Information("托盘退出：菜单已收起、托盘已释放 用时 {Ms}ms", exitWatch.ElapsedMilliseconds);

        // 第 3 步：同步泵一次调度器。IsOpen=false / Dispose 只是把「关闭 Popup」的活儿排进队列，
        // 这里以 Background 优先级抽干队列（更高优先级的关闭与渲染会先跑），让菜单先在屏幕上消失，
        // 再进入关窗与 Shutdown。
        // 注意：真正让菜单「赖着不走」的是随后的 Shutdown 收尾——见 ReminderEngine.Dispose() 的说明
        // （UI 线程上同步 Wait 引擎循环数秒）；那一处已改为非阻塞，本方法因此不再有阻塞点。
        Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

        _logger.Information("托盘退出：调度器队列已抽干 用时 {Ms}ms", exitWatch.ElapsedMilliseconds);

        // 第 4 步：关主窗口（绕过"关闭即隐藏"）并请求 WPF 关闭。
        // Shutdown() 会同步走完 OnExit 收尾，期间不再写日志（日志器将在收尾末段关闭），
        // 因此这里不记录「Shutdown 返回」；退出总时延由 OnExit 末尾那条汇总日志给出。
        _mainWindow?.ForceClose();
        Shutdown();
    }

    /// <summary>
    /// 收起托盘右键菜单（若已打开）。置 <c>IsOpen = false</c> 会让 ContextMenu 的 Popup 关闭；
    /// 菜单未打开时该赋值为无副作用的空操作。
    /// </summary>
    private void CloseTrayMenu()
    {
        if (_tray?.ContextMenu is { } menu)
        {
            menu.IsOpen = false;
        }
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
        var engine = _host!.Services.GetRequiredService<ReminderEngine>();
        var items = new ItemsViewModel(
            _host.Services.GetRequiredService<TaskService>(),
            engine,
            ReloadWidget,
            ShowTaskEditor,
            _logger);

        var viewModel = new MainWindowViewModel(
            _host.Services.GetRequiredService<ScheduleEditService>(),
            _host.Services.GetRequiredService<ScheduleService>(),
            _host.Services.GetRequiredService<PhaseService>(),
            engine,
            CreateSettingsViewModel(),
            new StatsViewModel(_host.Services.GetRequiredService<StatisticsService>(), _logger),
            items,
            ReloadWidget,
            ShowEntryEditor,
            ShowSubjectEditor,
            ShowPhaseManager,
            _logger);

        _mainWindowViewModel = viewModel;
        return new MainWindow(viewModel);
    }

    /// <summary>装配设置页视图模型：注入仓储、引擎，以及本宿主的三个即时生效回调。</summary>
    private SettingsViewModel CreateSettingsViewModel()
    {
        return new SettingsViewModel(
            _host!.Services.GetRequiredService<AppSettingRepository>(),
            _host.Services.GetRequiredService<ReminderEngine>(),
            ReloadWidget,
            ApplyWidgetOpacity,
            ApplyTheme,
            _logger);
    }

    /// <summary>把主题应用到整个应用（设置页即时切换与启动回放共用）。</summary>
    /// <param name="theme">主题存储值（dark / light）。</param>
    private void ApplyTheme(string theme) => _themeService?.Apply(theme);

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
        ApplyCaptionTheme(dialog);
        return dialog.ShowDialog() == true;
    }

    /// <summary>弹出事项编辑对话框，返回用户是否点「确定」（删除也会返回 true）。</summary>
    private bool ShowTaskEditor(TaskEditorViewModel viewModel)
    {
        var dialog = new TaskEditorDialog(viewModel) { Owner = ActiveOwnerWindow() };
        ApplyCaptionTheme(dialog);
        return dialog.ShowDialog() == true;
    }

    /// <summary>弹出新建科目对话框，返回用户是否点「确定」。</summary>
    private bool ShowSubjectEditor(SubjectEditorViewModel viewModel)
    {
        var dialog = new SubjectEditorDialog(viewModel) { Owner = ActiveOwnerWindow() };
        ApplyCaptionTheme(dialog);
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出阶段管理对话框。此对话框没有「确定」语义（操作即时落库），
    /// 故以 <see cref="PhaseManagerViewModel.HasChanges"/> 作为返回值：改过才算 true，调用方据此决定是否重排。
    /// </summary>
    /// <param name="viewModel">阶段管理视图模型。</param>
    private bool ShowPhaseManager(PhaseManagerViewModel viewModel)
    {
        var dialog = new PhaseManagerDialog(viewModel) { Owner = ActiveOwnerWindow() };
        ApplyCaptionTheme(dialog);
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 让对话框的系统标题栏跟随当前主题：句柄就绪后再请求 DWM 深色标题栏，
    /// 否则暗色主题下会露出白色标题栏（§4.4 标题栏口径）。
    /// </summary>
    /// <param name="dialog">即将以模态方式打开的窗口。</param>
    private void ApplyCaptionTheme(Window dialog)
    {
        dialog.SourceInitialized += (_, _) =>
            WindowChromeHelper.TryApplyCaptionTheme(dialog, _themeService?.IsDark ?? true);
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
