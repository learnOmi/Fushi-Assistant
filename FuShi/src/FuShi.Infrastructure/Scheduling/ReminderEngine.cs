﻿using FuShi.Domain;
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
    private readonly INotificationChannel _notifications;
    private readonly ILogger _logger;

    /// <summary>进程级停止信号。</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>重排信号；被 <see cref="RequestRescan"/> 取消后唤醒当前休眠。</summary>
    private CancellationTokenSource? _rescan;

    /// <summary>循环任务句柄。</summary>
    private Task? _loop;

    /// <summary>引擎侧数据发生变化的通知（课表展开出新实例、或某实例被触发），供宿主刷新悬浮组件。</summary>
    public event EventHandler? ScheduleChanged;

    /// <summary>构造引擎。</summary>
    /// <param name="schedule">课表服务（取数/写状态）。</param>
    /// <param name="notifications">提醒派发通道。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ReminderEngine(ScheduleService schedule, INotificationChannel notifications, ILogger logger)
    {
        _schedule = schedule;
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
    /// </summary>
    public void RequestRescan() => _rescan?.Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
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

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(STOP_WAIT_SECONDS));
        }
        catch (AggregateException ex)
        {
            // 循环以取消结束属预期；仍记录以便排查非预期异常。
            _logger.Debug(ex, "等待引擎循环收尾时捕获到异常");
        }

        _stop.Dispose();
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
                    _logger.Information("课表展开：新增 {Count} 条计划实例", created);
                    ScheduleChanged?.Invoke(this, EventArgs.Empty);
                }

                await ProcessDueAsync(since, now, token).ConfigureAwait(false);
                since = now;

                var upcoming = await _schedule.GetUpcomingAsync(now, token).ConfigureAwait(false);
                var delay = ComputeDelay(upcoming, now);
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
    /// </summary>
    /// <param name="since">本轮窗口起点。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="token">取消令牌。</param>
    private async Task ProcessDueAsync(DateTime since, DateTime now, CancellationToken token)
    {
        var due = await _schedule.GetDueAsync(since, now, token).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return;
        }

        var byId = due.ToDictionary(static item => item.Occurrence.Id);
        var verdict = MissedCompensator.Evaluate(
            due.Select(static item => item.Occurrence),
            now,
            since,
            TimeDefaults.DEFAULT_GRACE_MINUTES);

        foreach (var occurrence in verdict.ToResend)
        {
            token.ThrowIfCancellationRequested();
            await FireAsync(byId[occurrence.Id], occurrence.PlannedAt, token).ConfigureAwait(false);
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

    /// <summary>触发一条提醒：写账本状态 → 派发通知 → 通知宿主刷新悬浮组件。</summary>
    /// <param name="reminder">计划实例及其科目上下文。</param>
    /// <param name="plannedAt">计划触发时刻。</param>
    /// <param name="token">取消令牌。</param>
    private async Task FireAsync(PendingReminder reminder, DateTime plannedAt, CancellationToken token)
    {
        var firedAt = DateTime.Now;
        await _schedule.MarkFiredAsync(reminder.Occurrence.Id, firedAt, token).ConfigureAwait(false);

        var body = string.Format(EngineConstants.REMINDER_BODY_FORMAT, plannedAt.ToString(EngineConstants.CLOCK_FORMAT));
        _notifications.Dispatch(new ReminderMessage(
            reminder.SubjectName,
            body,
            ReminderStrength.Normal));

        _logger.Information("触发提醒：{Subject} @ {PlannedAt}", reminder.SubjectName, plannedAt);
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 由「未来最近触发点」算出下一次休眠时长，并夹在 [下限, 上限] 之间。
    /// 无待触发项时睡到上限，借此周期性重新展开课表视野。
    /// </summary>
    /// <param name="upcoming">未来待触发实例（按计划时刻升序）。</param>
    /// <param name="now">当前时刻。</param>
    private static TimeSpan ComputeDelay(IReadOnlyList<PendingReminder> upcoming, DateTime now)
    {
        var min = TimeSpan.FromMilliseconds(EngineConstants.MIN_SLEEP_MILLISECONDS);
        var max = TimeSpan.FromMinutes(EngineConstants.MAX_SLEEP_MINUTES);

        if (upcoming.Count == 0)
        {
            return max;
        }

        var delay = upcoming[0].Occurrence.PlannedAt - now;
        if (delay < min)
        {
            return min;
        }

        return delay > max ? max : delay;
    }
}
