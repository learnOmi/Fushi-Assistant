using FuShi.Domain;
using FuShi.Domain.Dedup;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Domain.Scheduling;
using FuShi.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 课表服务的<b>展开与对账</b>部分（M5c 拆分，纯移动不改行为）：
/// 把周期模板展开成账本实例并落库，以及编辑 / 阶段切换后按「改模板只影响未来」的口径重建未来实例。
/// </summary>
public sealed partial class ScheduleService
{
    /// <summary>
    /// 展开「当前生效阶段的课表」与「待办事项」，落库为 Occurrence（幂等）。
    /// 「幂等」由两层保证：算法层 <see cref="OccurrenceDeduplicator"/> 先剔除已存在的三元组，
    /// 数据库层 (SourceType, SourceId, PlannedAt) 唯一索引兜底并发/重放。
    /// <para>
    /// M4c 起事项与课表共用账本：即使没有生效阶段或课表为空，仍要展开事项实例，
    /// 因此这里不能像 M3 那样在无阶段时直接返回。
    /// </para>
    /// </summary>
    /// <param name="now">当前时刻（决定展开起点）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮新增的实例条数。</returns>
    public async Task<int> MaterializeAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var timeZone = await ResolveTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var from = DateOnly.FromDateTime(now);
        // Expand 的区间是「双闭区间」，故视野止点取 from + (视野天数 - 1)：
        // 这样恰好覆盖含今天在内的 EXPANSION_HORIZON_DAYS 天，同一个周几在视野内只落一次。
        var to = from.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var rangeStart = from.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = to.ToDateTime(TimeOnly.MaxValue);

        var created = 0;

        // ① 课表条目：无生效阶段或无条目时跳过，但不影响下面的事项展开。
        var phase = await GetActivePhaseAsync(context, cancellationToken).ConfigureAwait(false);
        if (phase is not null)
        {
            var entries = await context.ScheduleEntries
                .Where(e => e.PhaseId == phase.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entries.Count > 0)
            {
                var expansion = Expand(entries, phase, from, to, timeZone);
                created += await PersistExpansionAsync(context, expansion, rangeStart, rangeEnd, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // ② 事项（M4c）：只展开待办且有截止时刻的；截止为空的事项「只显示不提醒」。
        created += await MaterializeTasksAsync(context, from, to, rangeStart, rangeEnd, timeZone, cancellationToken)
            .ConfigureAwait(false);

        return created;
    }

    /// <summary>
    /// 课表对账（M3 技术核心，解决"幽灵提醒"）。
    /// 口径一句话：<b>「改模板只影响未来，已发生的账本不改写」</b>。
    /// <list type="number">
    /// <item>删除这些来源下「未来待触发」的 Occurrence：<c>Status == Pending</c> 且
    ///       （<c>PlannedAt &gt; now</c> 或 <c>ShiftMinutes &gt; 0</c>）。
    ///       后者是 M3c 的补充——被「稍后」顺延过的实例其 <c>PlannedAt</c> 虽已过，有效触发点仍在未来，同属"未来待触发"。</item>
    /// <item>按「顺延批次号」把<b>同批次</b>的其余实例一并回退（清零 <c>ShiftMinutes</c>、撤掉批次号），
    ///       消除"主角复原、配角还歪着"的残余错位（M3c）。回退只作用于仍 <c>Pending</c> 的行。</item>
    /// <item>若对应模板仍存在且启用，则在未来视野内重新展开并落库（幂等，唯一索引兜底）。</item>
    /// <item>不动 <c>PlannedAt &lt;= now</c> 且未顺延的 Pending（属"已发生"），不动任何 Fired / Missed / Skipped / Done。</item>
    /// </list>
    /// </summary>
    /// <param name="entryIds">本次编辑涉及的课表条目主键集合。</param>
    /// <param name="now">当前时刻（对账分界线）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重展开后新增落库的实例条数。</returns>
    public async Task<int> ReconcileAsync(
        IReadOnlyList<int> entryIds,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return 0;
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = entryIds.Distinct().ToList();

        // ① 先记下这些条目参与过的「顺延批次」——顺延可能把邻居也挪过，回退必须成批（必须在删除前取）。
        var touchedBatches = (await context.Occurrences
                .Where(o => o.SourceType == OwnerType.ScheduleEntry
                            && ids.Contains(o.SourceId)
                            && o.ShiftBatchId != null)
                .Select(o => o.ShiftBatchId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(static batch => batch.HasValue)
            .Select(static batch => batch!.Value)
            .Distinct()
            .ToList();

        // ② 删未来待触发实例（只动该来源；顺延过的 Pending 也算"未来待触发"，不能只看 PlannedAt）。
        var removed = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.ScheduleEntry
                        && ids.Contains(o.SourceId)
                        && o.Status == OccurrenceStatus.Pending
                        && (o.PlannedAt > now || o.ShiftMinutes > 0))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.Information("对账：清除 {Removed} 条未来待触发实例（来源 {Count} 个条目）", removed, ids.Count);

        // ③ 同批次的「配角」一并回退（清零顺延量、撤掉批次号）；只回退仍 Pending 的行，历史不回写。
        if (touchedBatches.Count > 0)
        {
            var rolledBack = await context.Occurrences
                .Where(o => o.SourceType == OwnerType.ScheduleEntry
                            && o.ShiftBatchId != null
                            && touchedBatches.Contains(o.ShiftBatchId!.Value)
                            && o.Status == OccurrenceStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(o => o.ShiftMinutes, 0)
                        .SetProperty(o => o.ShiftBatchId, (int?)null),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.Information("对账：回退 {RolledBack} 条同批次顺延实例（{BatchCount} 个批次）", rolledBack, touchedBatches.Count);
        }

        // ④ 若模板仍存在且属于生效阶段，则重新展开落库。
        var phase = await GetActivePhaseAsync(context, cancellationToken).ConfigureAwait(false);
        if (phase is null)
        {
            return 0;
        }

        var entries = await context.ScheduleEntries
            .Where(e => ids.Contains(e.Id) && e.PhaseId == phase.Id && e.Enabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            return 0;
        }

        var timeZone = await ResolveTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var from = DateOnly.FromDateTime(now);
        var to = from.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var expansion = Expand(entries, phase, from, to, timeZone);

        return await PersistExpansionAsync(
            context,
            expansion,
            from.ToDateTime(TimeOnly.MinValue),
            to.ToDateTime(TimeOnly.MaxValue),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <b>全量</b>对账（M4a 阶段切换专用）：先把所有来源为课表条目的「未来待触发」实例清掉，
    /// 再按当前生效阶段重新展开并落库。
    /// <para>
    /// 与 <see cref="ReconcileAsync"/> 的区别只在作用域：后者按「本次编辑涉及的条目」局部对账，
    /// 本方法不关心来源，整片未来都重建——阶段切换会整体换掉课表，局部对账无法覆盖旧阶段的全部条目。
    /// 历史记录（Fired / Done / Skipped / Missed）一律不动。
    /// </para>
    /// <para>
    /// 调用方必须在<b>切换生效阶段之后</b>调用，否则展开出来的还是旧阶段。
    /// </para>
    /// </summary>
    /// <param name="now">当前时刻（对账分界线与展开起点）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重新展开后新增落库的实例条数。</returns>
    public async Task<int> ReconcileAllAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // ① 清掉所有未来待触发实例（顺延过的 Pending 也属"未来待触发"，口径与局部对账一致）。
        var removed = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.ScheduleEntry
                        && o.Status == OccurrenceStatus.Pending
                        && (o.PlannedAt > now || o.ShiftMinutes > 0))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.Information("全量对账：清除 {Removed} 条未来待触发实例", removed);

        // ② 按切换后的生效阶段重新展开。
        var phase = await GetActivePhaseAsync(context, cancellationToken).ConfigureAwait(false);
        if (phase is null)
        {
            return 0;
        }

        var entries = await context.ScheduleEntries
            .Where(e => e.PhaseId == phase.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            return 0;
        }

        var timeZone = await ResolveTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var from = DateOnly.FromDateTime(now);
        var to = from.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var expansion = Expand(entries, phase, from, to, timeZone);

        return await PersistExpansionAsync(
            context,
            expansion,
            from.ToDateTime(TimeOnly.MinValue),
            to.ToDateTime(TimeOnly.MaxValue),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <b>事项对账</b>（M4c）：改标题/截止/重复/状态后，只重建该事项的未来实例。
    /// 口径与课表对账一致：<b>「改模板只影响未来，已发生的账本不改写」</b>。
    /// <list type="number">
    /// <item>删除该事项「未来待触发」的 Pending（<c>PlannedAt &gt; now</c> 或 <c>ShiftMinutes &gt; 0</c>）。</item>
    /// <item>事项仍为待办且截止非空时，在未来视野内重新展开并落库；已完成/已跳过/无截止则只清不建。</item>
    /// <item>历史（Fired / Done / Skipped / Missed）一律不动，统计口径不受编辑影响。</item>
    /// </list>
    /// </summary>
    /// <param name="taskId">事项主键。</param>
    /// <param name="now">当前时刻（对账分界线）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重新展开后新增落库的实例条数。</returns>
    public async Task<int> ReconcileTaskAsync(
        int taskId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var removed = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == taskId
                        && o.Status == OccurrenceStatus.Pending
                        && (o.PlannedAt > now || o.ShiftMinutes > 0))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.Information("事项对账：清除 {Removed} 条未来待触发实例（事项 {TaskId}）", removed, taskId);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null || task.Status != TaskItemStatus.Pending || task.DueAt is null)
        {
            return 0;
        }

        var timeZone = await ResolveTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var from = DateOnly.FromDateTime(now);
        var to = from.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var expansion = TaskRecurrenceExpander.Expand(task, from, to, timeZone);
        LogAdjustments(expansion.Adjustments);

        return await PersistExpansionAsync(
            context,
            expansion,
            from.ToDateTime(TimeOnly.MinValue),
            to.ToDateTime(TimeOnly.MaxValue),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 展开「待办且截止非空」的事项（M4c），落库为 Occurrence。
    /// 截止为空的事项只显示不提醒，因此不参与展开。
    /// </summary>
    private async Task<int> MaterializeTasksAsync(
        FuShiDbContext context,
        DateOnly from,
        DateOnly to,
        DateTime rangeStart,
        DateTime rangeEnd,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var tasks = await context.Tasks
            .Where(t => t.Status == TaskItemStatus.Pending && t.DueAt != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tasks.Count == 0)
        {
            return 0;
        }

        var expansion = TaskRecurrenceExpander.Expand(tasks, from, to, timeZone);
        if (expansion.Occurrences.Count == 0)
        {
            return 0;
        }

        LogAdjustments(expansion.Adjustments);
        return await PersistExpansionAsync(context, expansion, rangeStart, rangeEnd, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 把展开结果中「尚不存在」的实例落库（幂等）：算法层剔除已存在的三元组，
    /// 数据库层 (SourceType, SourceId, PlannedAt) 唯一索引兜底并发/重放。
    /// <see cref="MaterializeAsync"/> 与 <see cref="ReconcileAsync"/> 共用这段落库逻辑。
    /// </summary>
    /// <param name="context">当前短生命周期上下文。</param>
    /// <param name="expansion">展开结果。</param>
    /// <param name="rangeStart">查重窗口起（含）。</param>
    /// <param name="rangeEnd">查重窗口止（含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮新增的实例条数。</returns>
    private async Task<int> PersistExpansionAsync(
        FuShiDbContext context,
        ExpansionResult expansion,
        DateTime rangeStart,
        DateTime rangeEnd,
        CancellationToken cancellationToken)
    {
        if (expansion.Occurrences.Count == 0)
        {
            return 0;
        }

        var existing = await context.Occurrences
            .Where(o => o.PlannedAt >= rangeStart && o.PlannedAt <= rangeEnd)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fresh = OccurrenceDeduplicator.RemoveExisting(expansion.Occurrences, existing);
        if (fresh.Count == 0)
        {
            return 0;
        }

        context.Occurrences.AddRange(fresh.Select(static planned => new Occurrence
        {
            SourceType = planned.SourceType,
            SourceId = planned.SourceId,
            PlannedAt = planned.PlannedAt,
            Status = OccurrenceStatus.Pending,
        }));

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // 唯一索引冲突属预期兜底（并发/重放），记警告后放弃本轮的这批新增，不打断引擎。
            _logger.Warning(ex, "落库计划实例时命中唯一索引，本轮新增已跳过");
            return 0;
        }

        return fresh.Count;
    }

    /// <summary>取当前生效阶段（IsActive 中起始日最晚者）。无则返回 null。</summary>
    private static Task<Phase?> GetActivePhaseAsync(FuShiDbContext context, CancellationToken cancellationToken) =>
        context.Phases
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>展开课表（指定区间）并记录 DST 调整明细（无效/歧义时刻绝不静默丢弃）。</summary>
    private ExpansionResult Expand(
        IReadOnlyList<ScheduleEntry> entries,
        Phase phase,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone)
    {
        var result = RecurrenceExpander.Expand(entries, phase, from, to, timeZone);
        LogAdjustments(result.Adjustments);
        return result;
    }

    /// <summary>把展开过程中的 DST 调整写进日志。</summary>
    private void LogAdjustments(IReadOnlyList<DstAdjustment> adjustments)
    {
        foreach (var adjustment in adjustments)
        {
            _logger.Information(
                "DST 归一化：条目 {SourceId} 的 {Original:yyyy-MM-dd HH:mm} → {Adjusted:yyyy-MM-dd HH:mm}（{Kind}）",
                adjustment.SourceId,
                adjustment.Original,
                adjustment.Adjusted,
                adjustment.Kind);
        }
    }
}
