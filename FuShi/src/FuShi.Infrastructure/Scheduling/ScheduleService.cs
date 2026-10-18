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
/// 课表服务：把「周期模板」展开成具体实例，供调度引擎与悬浮组件共用。
/// 承担四件事：
/// <list type="number">
/// <item>展开落库：把当前生效阶段的课表展开成 <see cref="Occurrence"/> 账本行（幂等，靠唯一索引兜底）。</item>
/// <item>上屏时间线：展开出带科目名/颜色的 <see cref="TimelineSlot"/>，供悬浮组件展示。</item>
/// <item>引擎取数：给出「未来待触发」与「已到期待处理」的计划实例。</item>
/// <item>状态写入：把实例标记为已触发 / 已错过。</item>
/// </list>
/// 每次操作都用工厂创建**短生命周期**上下文：本服务会被后台引擎线程与 UI 线程分别调用。
/// </summary>
public sealed class ScheduleService
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly AppSettingRepository _settings;
    private readonly ILogger _logger;

    /// <summary>构造课表服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="settings">设置仓储（读取时区）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ScheduleService(
        IDbContextFactory<FuShiDbContext> contextFactory,
        AppSettingRepository settings,
        ILogger logger)
    {
        _contextFactory = contextFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// 展开当前生效阶段的课表并落库为 Occurrence（幂等）。
    /// 「幂等」由两层保证：算法层 <see cref="OccurrenceDeduplicator"/> 先剔除已存在的三元组，
    /// 数据库层 (SourceType, SourceId, PlannedAt) 唯一索引兜底并发/重放。
    /// </summary>
    /// <param name="now">当前时刻（决定展开起点）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮新增的实例条数。</returns>
    public async Task<int> MaterializeAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

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
        // Expand 的区间是「双闭区间」，故视野止点取 from + (视野天数 - 1)：
        // 这样恰好覆盖含今天在内的 EXPANSION_HORIZON_DAYS 天，同一个周几在视野内只落一次。
        var to = from.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1);
        var expansion = Expand(entries, phase, from, to, timeZone);
        if (expansion.Occurrences.Count == 0)
        {
            return 0;
        }

        var rangeStart = from.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = to.ToDateTime(TimeOnly.MaxValue);

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

    /// <summary>
    /// 展开出上屏时间线：给定日期区间内、带科目名与颜色的时段，按开始时刻升序。
    /// </summary>
    /// <param name="from">区间起（含）。</param>
    /// <param name="to">区间止（含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<TimelineSlot>> GetTimelineAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var phase = await GetActivePhaseAsync(context, cancellationToken).ConfigureAwait(false);
        if (phase is null)
        {
            return Array.Empty<TimelineSlot>();
        }

        var entries = await context.ScheduleEntries
            .Where(e => e.PhaseId == phase.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            return Array.Empty<TimelineSlot>();
        }

        var subjects = await context.Subjects
            .ToDictionaryAsync(s => s.Id, cancellationToken)
            .ConfigureAwait(false);

        var timeZone = await ResolveTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var expansion = Expand(entries, phase, from, to, timeZone);

        return expansion.Occurrences
            .Select(planned => new TimelineSlot(
                planned.PlannedAt,
                planned.EndsAt,
                planned.SubjectId,
                subjects.TryGetValue(planned.SubjectId, out var subject) ? subject.Name : ScheduleConstants.UNKNOWN_SUBJECT_NAME,
                subject?.Color ?? ModelDefaults.DEFAULT_SUBJECT_COLOR,
                planned.Note))
            .ToList();
    }

    /// <summary>
    /// 取「已到点、尚未处理」的计划实例：<paramref name="since"/> 到 <paramref name="now"/> 之间、状态为待触发者。
    /// 引擎每轮扫描调用它，再交给 <c>MissedCompensator</c> 做补发/错过判决。
    /// </summary>
    /// <param name="since">本轮扫描窗口起点（上次正常扫描时刻）。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PendingReminder>> GetDueAsync(
        DateTime since,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await QueryPendingAsync(context, since, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 取「未来待触发」的计划实例（按计划时刻升序），供引擎计算下一次休眠时长。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PendingReminder>> GetUpcomingAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.Status == OccurrenceStatus.Pending
                                && occurrence.FiredAt == null
                                && occurrence.PlannedAt > now
                          orderby occurrence.PlannedAt
                          select new { Occurrence = occurrence, SubjectName = subject.Name, entry.Note })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(static row => new PendingReminder(row.Occurrence, row.SubjectName, row.Note))
            .ToList();
    }

    /// <summary>把一条计划实例标记为已触发（写 FiredAt 与状态）。</summary>
    /// <param name="occurrenceId">实例主键。</param>
    /// <param name="firedAt">实际触发时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task MarkFiredAsync(int occurrenceId, DateTime firedAt, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.Occurrences
            .Where(o => o.Id == occurrenceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, OccurrenceStatus.Fired)
                    .SetProperty(o => o.FiredAt, (DateTime?)firedAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>批量把计划实例标记为已错过（补偿扫描中超出宽限期者）。</summary>
    /// <param name="occurrenceIds">实例主键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task MarkMissedAsync(IReadOnlyList<int> occurrenceIds, CancellationToken cancellationToken = default)
    {
        if (occurrenceIds.Count == 0)
        {
            return;
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.Occurrences
            .Where(o => occurrenceIds.Contains(o.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, OccurrenceStatus.Missed),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>取当前生效阶段（IsActive 中起始日最晚者）。无则返回 null。</summary>
    private static Task<Phase?> GetActivePhaseAsync(FuShiDbContext context, CancellationToken cancellationToken) =>
        context.Phases
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// 关联查询「待触发」的计划实例：Occurrence ⋈ ScheduleEntry ⋈ Subject。
    /// 只覆盖课表来源（M2 尚未产生事项来源的实例）。
    /// 注意：参数不取名 from/to——`from` 是查询表达式的上下文关键字，会与 `from ... in` 冲突。
    /// </summary>
    private static async Task<IReadOnlyList<PendingReminder>> QueryPendingAsync(
        FuShiDbContext context,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken)
    {
        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.Status == OccurrenceStatus.Pending
                                && occurrence.FiredAt == null
                                && occurrence.PlannedAt >= windowStart
                                && occurrence.PlannedAt <= windowEnd
                          orderby occurrence.PlannedAt
                          select new { Occurrence = occurrence, SubjectName = subject.Name, entry.Note })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(static row => new PendingReminder(row.Occurrence, row.SubjectName, row.Note))
            .ToList();
    }

    /// <summary>按当前设置解析时区；走了兜底则记一条警告，不静默降级。</summary>
    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken cancellationToken)
    {
        var setting = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var lookup = TimeZoneResolver.Resolve(setting.TimezoneId);
        if (lookup.UsedFallback)
        {
            _logger.Warning("时区 {TimezoneId} 未被直接识别，已回退到 {ResolvedId}", setting.TimezoneId, lookup.Zone.Id);
        }

        return lookup.Zone;
    }

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
