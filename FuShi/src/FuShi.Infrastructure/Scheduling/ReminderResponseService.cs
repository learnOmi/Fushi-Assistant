using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 提醒响应服务（M3b）：把用户对一条提醒的动作落进账本。
/// 它是 <see cref="Occurrence"/> 状态机与 <see cref="CompletionLog"/> 的<b>唯一写入方</b>，
/// 引擎的「进行中到点自动完成」也经它落账，避免两处各写一份完成记录逻辑。
/// <para>
/// 每次操作都用工厂创建<b>短生命周期</b>上下文：本服务会被 UI 线程（弹窗按钮）与后台引擎线程分别调用，
/// 内部 await 一律 <c>ConfigureAwait(false)</c>，使 UI 线程可 <c>GetAwaiter().GetResult()</c> 同步等待而不死锁。
/// </para>
/// 口径（与设计 5.4 一致）：
/// <list type="bullet">
/// <item>开始 → <c>InProgress</c>（开始时刻借用已有的 <c>FiredAt</c>），到 EndsAt 或手点完成才落 Done。</item>
/// <item>稍后 → 回退 <c>Pending</c> + 清 <c>FiredAt</c> + 链式叠 <c>ShiftMinutes</c>（当日顺延调课，M3c），到新触发点重弹。</item>
/// <item>跳过 → <c>Skipped</c> + 记一条 <c>Skipped</c>。</item>
/// <item>未响应 → 状态保持 <c>Fired</c>，只记一条 <c>Missed</c>。</item>
/// </list>
/// </summary>
public sealed class ReminderResponseService
{
    /// <summary>完成记录不写负数实际时长的下限保护。</summary>
    private const int MIN_ACTUAL_MINUTES = 0;

    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;

    /// <summary>构造响应服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ReminderResponseService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>按动作分派（弹窗回调用统一入口，避免 UI 侧写 switch）。</summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="action">用户动作。</param>
    /// <param name="now">当前时刻（本地墙钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RespondAsync(
        int occurrenceId,
        ReminderAction action,
        DateTime now,
        CancellationToken cancellationToken = default) => action switch
    {
        ReminderAction.Start => RespondStartAsync(occurrenceId, cancellationToken),
        ReminderAction.Snooze => RespondSnoozeAsync(occurrenceId, now, cancellationToken),
        ReminderAction.Skip => RespondSkipAsync(occurrenceId, cancellationToken),
        ReminderAction.Finish => RespondFinishAsync(occurrenceId, now, cancellationToken),
        ReminderAction.NoResponse => RecordNoResponseAsync(occurrenceId, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "未知的提醒动作"),
    };

    /// <summary>开始：置进行中（开始时刻沿用已写入的 <c>FiredAt</c>）。</summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RespondStartAsync(int occurrenceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await context.Occurrences
            .Where(o => o.Id == occurrenceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, OccurrenceStatus.InProgress),
                cancellationToken)
            .ConfigureAwait(false);

        LogIfMissing(affected, occurrenceId, nameof(RespondStartAsync));
    }

    /// <summary>
    /// 稍后（M3c：当日顺延调课）：把这条实例<b>回退成 Pending、清掉 FiredAt</b>，
    /// 并对「当天」的实例序列做<b>链式顺延</b>——被点的那条，加上真被它压到的后续课，
    /// 统一累加 <see cref="TimeDefaults.DEFAULT_SNOOZE_MINUTES"/> 分钟与同一个 <c>ShiftBatchId</c>。
    /// 于是新触发点 = <c>PlannedAt + ShiftMinutes − 提前量</c>，到点自然重弹；周期模板（周几+时间）不动。
    /// <para>
    /// 若该条自身触顶（顺延落点越过课程原结束时刻或当天 23:59）则<b>不动状态</b>——否则回退 Pending 会让它
    /// 在旧触发点立即重弹（即用户反馈过的"点了稍后反而立即弹"）。触顶只记日志。
    /// </para>
    /// </summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="now">当前时刻（仅作日志基准，顺延量以计划时刻为基准）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RespondSnoozeAsync(int occurrenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var target = await context.Occurrences
            .FirstOrDefaultAsync(o => o.Id == occurrenceId, cancellationToken)
            .ConfigureAwait(false);

        if (target is null)
        {
            LogIfMissing(0, occurrenceId, nameof(RespondSnoozeAsync));
            return;
        }

        // 事项（M4c）没有「同日课程序列」，顺延只作用于自己这一条，且只封顶当日 23:59。
        if (target.SourceType == OwnerType.Task)
        {
            await SnoozeTaskAsync(context, target, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 取「当天」的待触发/已触发实例（含被点那条与其后的课），组装成链式顺延输入。
        // 终态（Done/Skipped/Missed）与进行中不参与，避免回写历史或挪动已开始的课。
        var day = target.PlannedAt.Date;
        var dayEnd = day.AddDays(1);
        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.PlannedAt >= day
                                && occurrence.PlannedAt < dayEnd
                                && (occurrence.Status == OccurrenceStatus.Pending
                                    || occurrence.Status == OccurrenceStatus.Fired)
                          select new
                          {
                              occurrence.Id,
                              occurrence.PlannedAt,
                              occurrence.ShiftMinutes,
                              entry.EndTime,
                          })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(row => new ShiftChainItem(
                row.Id,
                row.PlannedAt,
                ComputeEndsAt(row.PlannedAt, row.EndTime),
                row.ShiftMinutes))
            .ToList();

        var result = ShiftChain.Apply(items, occurrenceId, TimeDefaults.DEFAULT_SNOOZE_MINUTES);

        if (result.CeilingReached)
        {
            _logger.Warning(
                "链式顺延触顶：实例 {OccurrenceId} 的某条顺延落点越过封顶（课程结束或当天 23:59），链条就此终止",
                occurrenceId);
        }

        if (result.Changes.Count == 0)
        {
            // 目标自身都没能顺延 → 不回退状态，避免在旧触发点立即重弹。
            _logger.Information(
                "稍后未生效：实例 {OccurrenceId} 自身触顶（顺延落点已越过课程结束或当天 23:59）",
                occurrenceId);
            return;
        }

        var batchId = await NextShiftBatchIdAsync(context, cancellationToken).ConfigureAwait(false);

        // 链上各条：写累计顺延量 + 同一个批次号（供对账成批回退）。
        foreach (var change in result.Changes)
        {
            var newShift = change.NewShiftMinutes;
            await context.Occurrences
                .Where(o => o.Id == change.OccurrenceId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(o => o.ShiftMinutes, newShift)
                        .SetProperty(o => o.ShiftBatchId, (int?)batchId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // 被点那条：回退成 Pending 并清 FiredAt，使它到新触发点重弹。
        await context.Occurrences
            .Where(o => o.Id == occurrenceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, OccurrenceStatus.Pending)
                    .SetProperty(o => o.FiredAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.Information(
            "稍后（当日顺延）：实例 {OccurrenceId} 顺延 {Minutes} 分钟，链上共 {Count} 条（批次 {BatchId}）",
            occurrenceId,
            TimeDefaults.DEFAULT_SNOOZE_MINUTES,
            result.Changes.Count,
            batchId);
    }

    /// <summary>跳过：置 Skipped 并记一条 Skipped 完成记录。</summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RespondSkipAsync(int occurrenceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await context.Occurrences
            .FirstOrDefaultAsync(o => o.Id == occurrenceId, cancellationToken)
            .ConfigureAwait(false);

        if (occurrence is null)
        {
            LogIfMissing(0, occurrenceId, nameof(RespondSkipAsync));
            return;
        }

        occurrence.Status = OccurrenceStatus.Skipped;
        if (occurrence.SourceType == OwnerType.Task)
        {
            await CompleteTaskAsync(context, occurrence, TaskItemStatus.Skipped, cancellationToken)
                .ConfigureAwait(false);
        }

        await AppendCompletionLogAsync(context, occurrence, CompletionStatus.Skipped, MIN_ACTUAL_MINUTES, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>完成：置 Done 并记一条 Done 完成记录，实际时长 = 现在 − 开始时刻（<c>FiredAt</c>）。</summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RespondFinishAsync(int occurrenceId, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await context.Occurrences
            .FirstOrDefaultAsync(o => o.Id == occurrenceId, cancellationToken)
            .ConfigureAwait(false);

        if (occurrence is null)
        {
            LogIfMissing(0, occurrenceId, nameof(RespondFinishAsync));
            return;
        }

        occurrence.Status = OccurrenceStatus.Done;

        // 事项没有「实际耗时」语义：统计只计入条数，实际时长固定 0，
        // 否则「提醒弹出后 3 分钟点完成」会被当成上了 3 分钟课。
        var isTask = occurrence.SourceType == OwnerType.Task;
        var actualMinutes = isTask
            ? MIN_ACTUAL_MINUTES
            : ComputeActualMinutes(occurrence.FiredAt, now);

        if (isTask)
        {
            await CompleteTaskAsync(context, occurrence, TaskItemStatus.Done, cancellationToken)
                .ConfigureAwait(false);
        }

        await AppendCompletionLogAsync(context, occurrence, CompletionStatus.Done, actualMinutes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>未响应：状态保持 Fired 不变，只记一条 Missed 完成记录。</summary>
    /// <param name="occurrenceId">账本行主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RecordNoResponseAsync(int occurrenceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await context.Occurrences
            .FirstOrDefaultAsync(o => o.Id == occurrenceId, cancellationToken)
            .ConfigureAwait(false);

        if (occurrence is null)
        {
            LogIfMissing(0, occurrenceId, nameof(RecordNoResponseAsync));
            return;
        }

        await AppendCompletionLogAsync(context, occurrence, CompletionStatus.Missed, MIN_ACTUAL_MINUTES, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 事项「稍后」：只顺延这一条，回退 Pending 并清 FiredAt，到新触发点重弹。
    /// 双重封顶只保留「当日 23:59」——事项没有时长，不存在「课程原结束时刻」这一层。
    /// 触顶时不动状态（否则会在旧触发点立即重弹）。
    /// </summary>
    private async Task SnoozeTaskAsync(
        FuShiDbContext context,
        Occurrence target,
        CancellationToken cancellationToken)
    {
        if (target.Status is OccurrenceStatus.Done or OccurrenceStatus.Skipped or OccurrenceStatus.Missed)
        {
            _logger.Debug("稍后：事项实例 {OccurrenceId} 已是终态 {Status}，忽略", target.Id, target.Status);
            return;
        }

        var newShift = target.ShiftMinutes + TimeDefaults.DEFAULT_SNOOZE_MINUTES;
        var shifted = target.PlannedAt.AddMinutes(newShift);
        var dayEnd = target.PlannedAt.Date.AddDays(1);

        if (shifted >= dayEnd)
        {
            _logger.Information(
                "稍后未生效：事项实例 {OccurrenceId} 顺延落点 {Shifted:yyyy-MM-dd HH:mm} 越过当日 23:59",
                target.Id,
                shifted);
            return;
        }

        await context.Occurrences
            .Where(o => o.Id == target.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.ShiftMinutes, newShift)
                    .SetProperty(o => o.Status, OccurrenceStatus.Pending)
                    .SetProperty(o => o.FiredAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.Information(
            "稍后（事项顺延）：实例 {OccurrenceId} 累计顺延 {Minutes} 分钟",
            target.Id,
            newShift);
    }

    /// <summary>
    /// 事项完成/跳过的统一收尾：任务状态置为 Done/Skipped，并删除「本条之后」的未来待触发实例。
    /// 重复事项到此结束，不再继续提醒；历史（本条及更早）不动。
    /// </summary>
    private static async Task CompleteTaskAsync(
        FuShiDbContext context,
        Occurrence occurrence,
        TaskItemStatus status,
        CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == occurrence.SourceId, cancellationToken)
            .ConfigureAwait(false);

        if (task is not null && task.Status == TaskItemStatus.Pending)
        {
            task.Status = status;
        }

        await context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == occurrence.SourceId
                        && o.Id != occurrence.Id
                        && o.Status == OccurrenceStatus.Pending
                        && o.PlannedAt > occurrence.PlannedAt)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 追加一条完成记录并保存：计划时长取模板「起止之间」的分钟数（跨午夜顺延），
    /// 日期取实例的计划日期，条目 = 实例来源。
    /// </summary>
    private async Task AppendCompletionLogAsync(
        FuShiDbContext context,
        Occurrence occurrence,
        CompletionStatus status,
        int actualMinutes,
        CancellationToken cancellationToken)
    {
        // 事项（M4c）：没有科目与计划时长，科目快照留空，归入统计页的「未分类」桶。
        // 注意必须按 SourceType 分流——若不分流，事项 Id 可能撞上同号的课表条目，把别人的科目写进历史。
        ScheduleEntry? entry = null;
        if (occurrence.SourceType != OwnerType.Task)
        {
            entry = await context.ScheduleEntries
                .FirstOrDefaultAsync(e => e.Id == occurrence.SourceId, cancellationToken)
                .ConfigureAwait(false);
        }

        var plannedMinutes = entry is null
            ? MIN_ACTUAL_MINUTES
            : ComputePlannedMinutes(entry.StartTime, entry.EndTime);

        context.CompletionLogs.Add(new CompletionLog
        {
            Date = DateOnly.FromDateTime(occurrence.PlannedAt),
            EntryId = occurrence.SourceId,
            // 科目快照（M4b）：落账时取「当时」的科目，之后条目被删或改科目都不会追溯改写历史统计。
            SubjectId = entry?.SubjectId,
            PlannedMin = plannedMinutes,
            ActualMin = actualMinutes,
            Status = status,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information(
            "提醒响应落账：实例 {OccurrenceId} 记为 {Status}（实际 {Actual} 分钟）",
            occurrence.Id,
            status,
            actualMinutes);
    }

    /// <summary>实际时长（分钟）：以 <c>FiredAt</c> 为开始时刻，负数按 0 兜底。</summary>
    private static int ComputeActualMinutes(DateTime? firedAt, DateTime now)
    {
        if (firedAt is null)
        {
            return MIN_ACTUAL_MINUTES;
        }

        var minutes = (int)Math.Round((now - firedAt.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        return minutes < MIN_ACTUAL_MINUTES ? MIN_ACTUAL_MINUTES : minutes;
    }

    /// <summary>计划时长（分钟）：EndTime ⩽ StartTime 视为跨午夜，顺延一天。</summary>
    private static int ComputePlannedMinutes(TimeOnly start, TimeOnly end)
    {
        var span = end.ToTimeSpan() - start.ToTimeSpan();
        if (span < TimeSpan.Zero)
        {
            span += TimeSpan.FromDays(1);
        }

        return (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 由课程计划时刻与模板结束时刻算出<b>结束时刻</b>；EndTime ⩽ StartTime 视为跨午夜，顺延到次日。
    /// 供链式顺延做重叠判定（与 <c>ScheduleService.ComputeEndsAt</c> 同口径）。
    /// </summary>
    private static DateTime ComputeEndsAt(DateTime plannedAt, TimeOnly endTime)
    {
        var endsAt = plannedAt.Date.Add(endTime.ToTimeSpan());
        return endsAt <= plannedAt ? endsAt.AddDays(1) : endsAt;
    }

    /// <summary>
    /// 生成一个新的顺延批次号：取库中现存批次号的最大值 + 1。
    /// 批次号只是「同一操作内成组」的标记，回退清零后即可复用，无需全局唯一。
    /// </summary>
    private static async Task<int> NextShiftBatchIdAsync(FuShiDbContext context, CancellationToken cancellationToken)
    {
        var max = await context.Occurrences
            .Where(o => o.ShiftBatchId != null)
            .MaxAsync(o => (int?)o.ShiftBatchId, cancellationToken)
            .ConfigureAwait(false);

        return (max ?? 0) + 1;
    }

    /// <summary>
    /// 受影响行数为 0 说明实例已不存在（通常是改课表后被对账删除）。
    /// 这不是错误——记一条调试日志后静默返回，绝不让弹窗因为找不到账本行而崩。
    /// </summary>
    private void LogIfMissing(int affected, int occurrenceId, string operation)
    {
        if (affected == 0)
        {
            _logger.Debug("响应 {Operation}：实例 {OccurrenceId} 不存在（可能已被对账删除），已忽略", operation, occurrenceId);
        }
    }
}
