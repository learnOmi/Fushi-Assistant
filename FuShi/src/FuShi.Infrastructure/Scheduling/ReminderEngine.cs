using FuShi.Domain;
using FuShi.Domain.Compensation;
using FuShi.Domain.Enums;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 提醒引擎：后台线程上的单循环调度器（设计文档 3.4 / ADR-6「睡到下一触发点」）。
/// 循环形状固定为：
/// <code>
/// 每轮 → 展开课表落库 → 扫描到期项（补偿判决 + 触发）→ 取未来最近触发点 → 睡到那一刻
/// 被 RequestRescan() 打断 → 立即进入下一轮（重新展开 + 重新扫描）
/// </code>
/// <para>
/// 关键设计：<b>补偿扫描放在每轮顶部</b>，因此休眠/关机/改时钟/DST 跳变导致的错过都能在下一轮被兜住，
/// 而不是只依赖系统唤醒事件。系统唤醒事件处理器只调 <see cref="RequestRescan"/>（仅取消 CTS），
/// 绝不触碰 UI 或 DbContext——该事件跑在系统隐藏窗口的消息泵线程上。
/// </para>
/// 用 <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 而非 <c>PeriodicTimer</c>：后者是固定周期，
/// 退化为轮询，违背 ADR-6 且耗电（对设计文档 4.2 表格的<span>有意修正</span>）。
/// </summary>
public sealed class ReminderEngine : IDisposable
{
    /// <summary>停止引擎等待循环收尾的上限（秒）。</summary>
    private const int STOP_WAIT_SECONDS = 3;

    private readonly ScheduleService _schedule;
    private readonly ReminderResponseService _response;
    private readonly INotificationChannel _notifications;
    private readonly ILogger _logger;

    /// <summary>进程级停止信号。</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>重排信号；被 <see cref="RequestRescan"/> 取消后唤醒当前休眠。</summary>
    private CancellationTokenSource? _rescan;

    /// <summary>循环任务句柄。</summary>
    private Task? _loop;

    /// <summary>释放标记（0 = 未释放，1 = 已释放）。用 <see cref="Interlocked"/> 保证 Dispose 幂等。</summary>
    private int _disposed;

    /// <summary>引擎侧数据发生变化的通知（课表展开出新实例、或某实例被触发），供宿主刷新悬浮组件。</summary>
    public event EventHandler? ScheduleChanged;

    /// <summary>构造引擎。</summary>
    /// <param name="schedule">课表服务（取数/写状态）。</param>
    /// <param name="response">提醒响应服务（「进行中」到点自动收成 Done 也经它落账）。</param>
    /// <param name="notifications">提醒派发通道。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ReminderEngine(
        ScheduleService schedule,
        ReminderResponseService response,
        INotificationChannel notifications,
        ILogger logger)
    {
        _schedule = schedule;
        _response = response;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>启动引擎（幂等：重复调用不产生第二个循环）。</summary>
    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>
    /// 请求重新展开与重扫：取消当前休眠，使循环立即进入下一轮。
    /// 仅做「取消 CTS」这一件事，线程安全；系统唤醒事件处理器可直接调用。
    /// <para>
    /// 与循环自身的 <c>finally</c>（会 <c>_rescan.Dispose()</c> 并置空）存在天然竞态：
    /// 若恰好读到「已被释放但尚未置空」的实例，<c>Cancel()</c> 会抛 <see cref="ObjectDisposedException"/>。
    /// 该调用点跑在系统隐藏窗口的消息泵线程上，异常逃逸后果不可控，故此处就地兜住（重排本就是幂等的，丢掉这一次取消也无碍）。
    /// </para>
    /// </summary>
    public void RequestRescan()
    {
        try
        {
            _rescan?.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            // 竞态：循环刚释放了重排信号。下一轮仍会因课表变更而重排，此处忽略即可。
            _logger.Debug(ex, "请求重排时重排信号已先行释放");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>幂等</b>：本类以 <c>AddSingleton</c> 注册，退出路径上宿主会先显式释放一次，随后
    /// <c>IHost.Dispose()</c> 还会释放容器内的<b>同一个</b>实例。若不保护，第二次 <c>_stop.Cancel()</c>
    /// 会对已释放的 CTS 抛 <see cref="ObjectDisposedException"/>，把整个退出收尾流程打断
    /// （M3c 实测缺陷 4：日志里「浮时退出」缺失、进程残留）。
    /// <para>
    /// <b>不在调用线程上等待循环收尾</b>：退出路径跑在 UI 线程上，同步 <c>Wait</c> 最多会把 UI 线程
    /// 占死 <see cref="STOP_WAIT_SECONDS"/> 秒，期间已排队的托盘菜单关闭/渲染消息无法被处理，
    /// 界面表现就是「点了退出，菜单赖在屏幕上几秒才消失」。因此这里只发停止信号，
    /// 收尾等待与 CTS 释放一并甩到线程池，绝不阻塞调用方。
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // 幂等保护：第二次及以后的调用直接返回（容器二次释放是常态，不是异常）。
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 停止信号：主循环在这行之后会从 Task.Delay(…) 上立刻抛取消并退出（非阻塞）。
        _stop.Cancel();

        try
        {
            _rescan?.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            // 重排 CTS 可能已被循环自身释放，属正常竞态；记一条调试日志即可。
            _logger.Debug(ex, "释放重排信号时 CTS 已先行释放");
        }

        var loop = _loop;
        if (loop is null || loop.IsCompleted)
        {
            // 循环从未启动或已经结束：可以直接释放信号源。
            DisposeStopSource();
            return;
        }

        // 有循环在跑：把「等它收尾 → 释放 CTS」交给线程池。
        // 刻意不在进入此分之后写日志：本回调可能在宿主已释放日志器之后才跑完，
        // 届时再写日志会二次抛错（退出末段的日志器已被关闭），故收尾阶段保持静默。
        _ = Task.Run(() =>
        {
            try
            {
                loop.Wait(TimeSpan.FromSeconds(STOP_WAIT_SECONDS));
            }
            catch (AggregateException)
            {
                // 循环以取消结束属预期形态：此处只做收尾，不再上报。
            }

            DisposeStopSource();
        });
    }

    /// <summary>
    /// 释放进程级停止信号源。被重复调用（或与循环释放产生竞态）时静默忽略，不写日志
    /// ——调用方可能在宿主已释放日志器之后才执行到这里。
    /// </summary>
    private void DisposeStopSource()
    {
        try
        {
            _stop.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 已释放：幂等收尾，无需处理。
        }
    }

    /// <summary>引擎主循环。每轮做一次「展开 → 扫描 → 睡到下一触发点」，被重排打断则立刻重来。</summary>
    /// <param name="stopToken">进程级停止令牌。</param>
    private async Task RunAsync(CancellationToken stopToken)
    {
        _logger.Information("提醒引擎启动");

        // 启动首轮的回溯窗口：把进程不在时错过的提醒交给补偿扫描处理。
        var since = DateTime.Now.AddHours(-EngineConstants.STARTUP_LOOKBACK_HOURS);

        while (!stopToken.IsCancellationRequested)
        {
            _rescan = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
            var token = _rescan.Token;

            try
            {
                var now = DateTime.Now;

                var created = await _schedule.MaterializeAsync(now, token).ConfigureAwait(false);
                if (created > 0)
                {
                    _logger.Information("展开完成：新增 {Count} 条计划实例（课表 + 事项）", created);
                    ScheduleChanged?.Invoke(this, EventArgs.Empty);
                }

                await ProcessDueAsync(since, now, token).ConfigureAwait(false);
                await ProcessCompletionsAsync(now, token).ConfigureAwait(false);
                since = now;

                var upcoming = await _schedule.GetUpcomingAsync(now, token).ConfigureAwait(false);
                var nextCompletion = await _schedule.GetNextCompletionAtAsync(now, token).ConfigureAwait(false);
                var delay = ComputeDelay(upcoming, nextCompletion, now);
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
            {
                // 被 RequestRescan 打断（课表变更 / 系统唤醒）→ 立即重来一轮。
                _logger.Information("调度重排：立即重新展开与扫描");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "提醒引擎循环异常，{Delay}ms 后重试", EngineConstants.ERROR_RETRY_DELAY_MILLISECONDS);

                try
                {
                    await Task.Delay(EngineConstants.ERROR_RETRY_DELAY_MILLISECONDS, stopToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                _rescan?.Dispose();
                _rescan = null;
            }
        }

        _logger.Information("提醒引擎停止");
    }

    /// <summary>
    /// 扫描本轮窗口 <c>[since, now]</c> 内到期的实例，走错过补偿判决：
    /// 宽限期内 → 立即补发；超出宽限 → 标记错过并合并一条摘要。
    /// <para>
    /// 窗口下界<b>严格取本轮起点 <paramref name="since"/></b>，不再向前放宽到宽限期。
    /// 曾一度放宽为 <c>min(since, now − 宽限期)</c> 以兜住「实例在触发点之后才落库」的迟到实例，
    /// 但该口径会让「临近开场才新建/重存课表条目」的场景在保存瞬间立刻补弹一条提醒
    /// （M3b 实测缺陷：用户点「稍后」后重存条目，旧稍后被对账取消、新条目又立刻补发，
    /// 界面表现为「稍后/跳过按钮没有作用」）。现按用户口径回退：<b>补偿只覆盖「本轮扫描窗口内到达的触发点」</b>。
    /// </para>
    /// <para>
    /// 取舍：进程关闭/休眠期间错过的提醒仍由启动回溯窗口兜住
    /// （启动首轮 <c>since = now − STARTUP_LOOKBACK_HOURS</c>）；代价是「在提前量之内才新建的课」不再补发提醒，
    /// 属本次回退的预期行为。
    /// </para>
    /// </summary>
    /// <param name="since">本轮窗口起点（启动首轮为回溯窗口起点）。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="token">取消令牌。</param>
    private async Task ProcessDueAsync(DateTime since, DateTime now, CancellationToken token)
    {
        var grace = TimeDefaults.DEFAULT_GRACE_MINUTES;

        var due = await _schedule.GetDueAsync(since, now, token).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return;
        }

        // 补偿判决按「实际触发时刻」比对：提前量逐条取（M4c 起事项可挂自己的规则），
        // 顺延量（Occurrence.ShiftMinutes）仍由补偿算法内部消化。
        var byId = due.ToDictionary(static item => item.Occurrence.Id);
        var verdict = MissedCompensator.Evaluate(
            due.Select(static item => (item.Occurrence, item.LeadMinutes)),
            now,
            since,
            grace);

        foreach (var occurrence in verdict.ToResend)
        {
            token.ThrowIfCancellationRequested();
            await FireAsync(byId[occurrence.Id], token).ConfigureAwait(false);
        }

        if (verdict.ToMarkMissed.Count > 0)
        {
            var ids = verdict.ToMarkMissed.Select(static occurrence => occurrence.Id).ToList();
            await _schedule.MarkMissedAsync(ids, token).ConfigureAwait(false);
            _logger.Information("错过补偿：{Count} 条超出宽限期，已标记为错过", ids.Count);

            if (verdict.SummaryText is not null)
            {
                _notifications.Dispatch(new ReminderMessage(
                    EngineConstants.SUMMARY_TITLE,
                    verdict.SummaryText,
                    ReminderStrength.Normal));
            }

            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 收起「进行中」且已到结束时刻的实例：写 Done 与 CompletionLog（M3b）。
    /// 每轮在到期扫描之后执行，保证「开始」后即使应用一直开着也能自动收账。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    /// <param name="token">取消令牌。</param>
    private async Task ProcessCompletionsAsync(DateTime now, CancellationToken token)
    {
        var completions = await _schedule.GetDueCompletionsAsync(now, token).ConfigureAwait(false);
        if (completions.Count == 0)
        {
            return;
        }

        foreach (var occurrence in completions)
        {
            token.ThrowIfCancellationRequested();
            await _response.RespondFinishAsync(occurrence.Id, now, token).ConfigureAwait(false);
        }

        _logger.Information("进行中收尾：{Count} 条到点自动完成", completions.Count);
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>触发一条提醒：写账本状态 → 派发通知 → 通知宿主刷新悬浮组件。</summary>
    /// <param name="reminder">计划实例及其科目上下文。</param>
    /// <param name="token">取消令牌。</param>
    private async Task FireAsync(PendingReminder reminder, CancellationToken token)
    {
        var firedAt = DateTime.Now;
        await _schedule.MarkFiredAsync(reminder.Occurrence.Id, firedAt, token).ConfigureAwait(false);

        // 正文展示「课程开始时刻 / 事项截止时刻」（PlannedAt），而非实际触发点——提前量/稍后都不改变用户认知里的时间。
        var plannedAt = reminder.Occurrence.PlannedAt;
        var bodyFormat = reminder.IsTask
            ? EngineConstants.TASK_REMINDER_BODY_FORMAT
            : EngineConstants.REMINDER_BODY_FORMAT;
        var body = string.Format(bodyFormat, plannedAt.ToString(EngineConstants.CLOCK_FORMAT));
        _notifications.Dispatch(new ReminderMessage(
            reminder.SubjectName,
            body,
            ReminderStrength.Normal,
            reminder.Occurrence.Id,
            plannedAt,
            reminder.EndsAt,
            reminder.IsTask));

        _logger.Information("触发提醒：{Subject} @ {PlannedAt}", reminder.SubjectName, plannedAt);
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 由「未来最近触发点」算出下一次休眠时长，并夹在 [下限, 上限] 之间。
    /// 无待触发项时睡到上限，借此周期性重新展开课表视野。
    /// </summary>
    /// <param name="upcoming">未来待触发实例（按<b>实际触发点</b>升序：计划时刻 + 顺延量 − 提前量）。</param>
    /// <param name="nextCompletionAt">最早的「进行中」结束时刻（无则 null），用于准点自动收成 Done。</param>
    /// <param name="now">当前时刻。</param>
    private static TimeSpan ComputeDelay(
        IReadOnlyList<PendingReminder> upcoming,
        DateTime? nextCompletionAt,
        DateTime now)
    {
        var min = TimeSpan.FromMilliseconds(EngineConstants.MIN_SLEEP_MILLISECONDS);
        var max = TimeSpan.FromMinutes(EngineConstants.MAX_SLEEP_MINUTES);

        // 睡到「下一个待触发点」与「下一个进行中结束时刻」二者较早者。
        // 必须用 TriggerAt 而非 PlannedAt：顺延实例的触发点是 PlannedAt + ShiftMinutes − 提前量，
        // 若误用 PlannedAt 会算出负时长 → 退化为忙等（M3b/M3c 修正）。
        DateTime? next = upcoming.Count > 0 ? upcoming[0].TriggerAt : null;
        if (nextCompletionAt is { } completion && (next is null || completion < next.Value))
        {
            next = completion;
        }

        if (next is null)
        {
            return max;
        }

        var delay = next.Value - now;
        if (delay < min)
        {
            return min;
        }

        return delay > max ? max : delay;
    }
}
