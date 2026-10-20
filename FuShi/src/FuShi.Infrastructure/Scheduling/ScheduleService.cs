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

        return await PersistExpansionAsync(
            context,
            expansion,
            from.ToDateTime(TimeOnly.MinValue),
            to.ToDateTime(TimeOnly.MaxValue),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 取周网格用的「模板视图」：当前阶段下的课表条目（关联科目名/颜色），供纯算法
    /// <see cref="WeekGridProjector"/> 投影成 7×N 网格。与 <see cref="GetTimelineAsync"/> 的区别：
    /// 后者返回「已展开到具体日期」的实例，前者保留「周几」的模板语义。
    /// <para>
    /// M3c 起，额外把<b>今天</b>被「稍后」顺延过的实例的 <c>ShiftMinutes</c> 叠到对应条目上（其余六天保持模板），
    /// 使主窗口网格的今天这一列能显示顺延后的时段与「已顺延」角标。
    /// </para>
    /// </summary>
    /// <param name="phaseId">阶段主键。</param>
    /// <param name="now">当前时刻（界定"今天"并供投影判定"当前"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<WeekGridSource>> GetWeekGridAsync(
        int phaseId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await (from entry in context.ScheduleEntries
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where entry.PhaseId == phaseId
                          orderby entry.StartTime
                          select new
                          {
                              entry.Id,
                              entry.DayOfWeek,
                              entry.StartTime,
                              entry.EndTime,
                              entry.Note,
                              entry.Enabled,
                              SubjectName = subject.Name,
                              SubjectColor = subject.Color,
                          })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 「今天」的账本叠加：只取今天落到账本、且被顺延过或已跳过的实例（两者都只影响今天这一列）。
        var todayStart = now.Date;
        var todayEnd = todayStart.AddDays(1);

        var shiftedToday = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.ScheduleEntry
                        && o.ShiftMinutes > 0
                        && o.PlannedAt >= todayStart
                        && o.PlannedAt < todayEnd)
            .Select(o => new { o.SourceId, o.ShiftMinutes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var shiftByEntry = shiftedToday
            .GroupBy(static o => o.SourceId)
            .ToDictionary(static group => group.Key, static group => group.Max(o => o.ShiftMinutes));

        // 「今天」被跳过的实例：透出到网格今日列，界面据此显示「已跳过」角标（比「已顺延」更终态）。
        var skippedToday = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.ScheduleEntry
                        && o.Status == OccurrenceStatus.Skipped
                        && o.PlannedAt >= todayStart
                        && o.PlannedAt < todayEnd)
            .Select(o => o.SourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var skippedEntries = skippedToday.ToHashSet();

        return rows
            .Select(row => new WeekGridSource(
                row.Id,
                row.DayOfWeek,
                row.StartTime,
                row.EndTime,
                row.SubjectName,
                row.SubjectColor,
                row.Note,
                row.Enabled,
                shiftByEntry.GetValueOrDefault(row.Id),
                skippedEntries.Contains(row.Id)))
            .ToList();
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

        // 账本状态投影（M3b 可见反馈）：把同一窗口内的 Occurrence 按 (来源条目, 计划时刻) 索引，
    // 供悬浮组件展示「已跳过 / 已顺延」。展开本身不带状态，故此处做一次「左连接式」补全。
    var rangeStart = from.ToDateTime(TimeOnly.MinValue);
    var rangeEnd = to.ToDateTime(TimeOnly.MaxValue);
    var ledger = await context.Occurrences
        .Where(o => o.SourceType == OwnerType.ScheduleEntry
                    && o.PlannedAt >= rangeStart
                    && o.PlannedAt <= rangeEnd)
        .Select(o => new { o.SourceId, o.PlannedAt, o.Status, o.ShiftMinutes })
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

    // (SourceType, SourceId, PlannedAt) 唯一索引保证键不重复。
    var ledgerByKey = ledger.ToDictionary(
        static row => (row.SourceId, row.PlannedAt),
        static row => (row.Status, row.ShiftMinutes));

    return expansion.Occurrences
        .Select(planned =>
        {
            var hasLedger = ledgerByKey.TryGetValue((planned.SourceId, planned.PlannedAt), out var state);
            var shiftMinutes = hasLedger ? state.ShiftMinutes : 0;
            return new TimelineSlot(
                planned.PlannedAt.AddMinutes(shiftMinutes),
                planned.EndsAt.AddMinutes(shiftMinutes),
                planned.SubjectId,
                subjects.TryGetValue(planned.SubjectId, out var subject) ? subject.Name : ScheduleConstants.UNKNOWN_SUBJECT_NAME,
                subject?.Color ?? ModelDefaults.DEFAULT_SUBJECT_COLOR,
                planned.Note,
                hasLedger ? state.Status : null,
                shiftMinutes);
        })
        .ToList();
    }

    /// <summary>
    /// 取本轮窗口 <c>[since, now]</c> 内「已到触发点、尚未处理」的计划实例。
    /// 引擎每轮扫描调用它，再交给 <c>MissedCompensator</c> 做补发/错过判决。
    /// <para>
    /// 窗口按<b>实际触发时刻</b>而非 PlannedAt 判定（M3b 提前量 + M3c 顺延，见 <see cref="PendingReminder.TriggerAt"/>）：
    /// 有效触发点 = <c>PlannedAt + ShiftMinutes - 提前量</c>，故对 PlannedAt 的过滤窗口整体后移提前量。
    /// 过滤一律对「列」做阈值加减、不对列调用 <c>AddMinutes</c>，保证可走索引与 SQL 翻译。
    /// M3c 顺延会把有效触发点往后推：下界按 <see cref="TimeDefaults.SHIFT_WINDOW_SLACK_MINUTES"/> 放宽，
    /// 拉回候选行后再在内存里按有效时刻精筛（上界无需放宽——顺延只让时间变晚）。
    /// </para>
    /// </summary>
    /// <param name="since">本轮扫描窗口起点（上次正常扫描时刻）。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PendingReminder>> GetDueAsync(
        DateTime since,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var lead = await GetLeadMinutesAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // 有效触发点 ∈ [since, now]。因 ShiftMinutes 会把触发点往后移，SQL 下界放宽一个常量（内存再精筛）。
        var pendingLower = since.AddMinutes(lead - TimeDefaults.SHIFT_WINDOW_SLACK_MINUTES);
        var pendingUpper = now.AddMinutes(lead);

        var rows = await SelectPendingRowsAsync(context, pendingLower, pendingUpper, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => Map(row.Occurrence, row.SubjectName, row.Note, row.EndTime, lead))
            .Where(reminder => reminder.TriggerAt >= since && reminder.TriggerAt <= now)
            .OrderBy(static reminder => reminder.TriggerAt)
            .ToList();
    }

    /// <summary>
    /// 取「未来待触发」的计划实例（按<b>实际触发时刻</b>升序），供引擎计算下一次休眠时长。
    /// M3c 顺延后，有效触发点 = <c>PlannedAt + ShiftMinutes - 提前量</c>，故 SQL 下界放宽一个常量后内存精筛
    /// （被「稍后」顺延过的实例因此仍会被纳入——它的新触发点在未来）。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PendingReminder>> GetUpcomingAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var lead = await GetLeadMinutesAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // 有效触发点 > now；因 ShiftMinutes 会把触发点往后移，SQL 下界放宽一个常量（内存再精筛）。
        var pendingCutoff = now.AddMinutes(lead - TimeDefaults.SHIFT_WINDOW_SLACK_MINUTES);

        var rows = await SelectUpcomingRowsAsync(context, pendingCutoff, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => Map(row.Occurrence, row.SubjectName, row.Note, row.EndTime, lead))
            .Where(reminder => reminder.TriggerAt > now)
            .OrderBy(static reminder => reminder.TriggerAt)
            .ToList();
    }

    /// <summary>
    /// 读全局提前提醒分钟数（<c>AppSetting.LeadTimeMinutes</c>）。
    /// 非正值一律按 0 兜底，保证提前量永不把触发点推到计划时刻之后。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<int> GetLeadMinutesAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        return setting.LeadTimeMinutes > 0 ? setting.LeadTimeMinutes : 0;
    }

    /// <summary>取「进行中且已到结束时刻」的实例（M3b：到 EndsAt 自动收成 Done）。</summary>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<Occurrence>> GetDueCompletionsAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await SelectInProgressAsync(context, cancellationToken).ConfigureAwait(false);
        return rows.Where(row => row.EndsAt <= now).Select(static row => row.Occurrence).ToList();
    }

    /// <summary>
    /// 取「进行中实例里最早的结束时刻」（&gt; now 才有意义），供引擎把休眠睡到该时刻，
    /// 使「开始」后能准点自动收成 Done，而不是干等到下一个待触发点或 60 分钟上限。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最早的未来结束时刻；无进行中实例时为 null。</returns>
    public async Task<DateTime?> GetNextCompletionAtAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await SelectInProgressAsync(context, cancellationToken).ConfigureAwait(false);
        var future = rows.Where(row => row.EndsAt > now).Select(static row => row.EndsAt).ToList();
        return future.Count == 0 ? null : future.Min();
    }

    /// <summary>
    /// 关联查询全部「进行中」实例及其结束时刻。
    /// 结束时刻（EndsAt）由「展开时就已确定的时间」在内存里算出，故不走 SQL 翻译（跨午夜顺延见 <see cref="ComputeEndsAt"/>）。
    /// </summary>
    /// <param name="context">当前短生命周期上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IReadOnlyList<(Occurrence Occurrence, DateTime EndsAt)>> SelectInProgressAsync(
        FuShiDbContext context,
        CancellationToken cancellationToken)
    {
        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.Status == OccurrenceStatus.InProgress
                          select new { Occurrence = occurrence, entry.EndTime })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => (row.Occurrence, ComputeEndsAt(row.Occurrence.PlannedAt, row.EndTime, row.Occurrence.ShiftMinutes)))
            .ToList();
    }

    /// <summary>把一条计划实例标记为已触发（写状态与 FiredAt）。</summary>
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

    /// <summary>查询行：账本行 + 上屏/组装所需的模板与科目上下文。</summary>
    private sealed record PendingRow(Occurrence Occurrence, string SubjectName, string? Note, TimeOnly EndTime);

    /// <summary>
    /// 关联查询「本轮到期」的候选行：Occurrence ⋈ ScheduleEntry ⋈ Subject。
    /// 谓词见 <see cref="GetDueAsync"/> 注释：待触发实例按计划时刻落在放宽后的窗口内（有效触发点由内存精筛）。
    /// 注意：参数不取名 from/to——`from` 是查询表达式的上下文关键字，会与 `from ... in` 冲突。
    /// </summary>
    private static async Task<IReadOnlyList<PendingRow>> SelectPendingRowsAsync(
        FuShiDbContext context,
        DateTime pendingLower,
        DateTime pendingUpper,
        CancellationToken cancellationToken)
    {
        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.Status == OccurrenceStatus.Pending
                                && occurrence.FiredAt == null
                                && occurrence.PlannedAt >= pendingLower
                                && occurrence.PlannedAt <= pendingUpper
                          select new PendingRow(occurrence, subject.Name, entry.Note, entry.EndTime))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    /// <summary>
    /// 关联查询「未来待触发」的候选行：待触发实例计划时刻在 <paramref name="pendingCutoff"/> 之后
    /// （有效触发点由内存精筛）。
    /// </summary>
    private static async Task<IReadOnlyList<PendingRow>> SelectUpcomingRowsAsync(
        FuShiDbContext context,
        DateTime pendingCutoff,
        CancellationToken cancellationToken)
    {
        var rows = await (from occurrence in context.Occurrences
                          join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where occurrence.SourceType == OwnerType.ScheduleEntry
                                && occurrence.Status == OccurrenceStatus.Pending
                                && occurrence.FiredAt == null
                                && occurrence.PlannedAt > pendingCutoff
                          select new PendingRow(occurrence, subject.Name, entry.Note, entry.EndTime))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    /// <summary>把查询行组装成引擎用的计划实例，并算出有效触发点与有效结束时刻。</summary>
    private static PendingReminder Map(
        Occurrence occurrence,
        string subjectName,
        string? note,
        TimeOnly endTime,
        int leadMinutes)
    {
        return new PendingReminder(
            occurrence,
            subjectName,
            note,
            EffectiveTriggerAt(occurrence, leadMinutes),
            ComputeEndsAt(occurrence.PlannedAt, endTime, occurrence.ShiftMinutes));
    }

    /// <summary>
    /// 一条实例的<b>有效触发时刻</b>（M3b 提前量 + M3c 顺延）：
    /// 普通实例 = <c>PlannedAt + ShiftMinutes − 提前量</c>。
    /// </summary>
    private static DateTime EffectiveTriggerAt(Occurrence occurrence, int leadMinutes) =>
        occurrence.PlannedAt.AddMinutes(occurrence.ShiftMinutes - leadMinutes);

    /// <summary>
    /// 由课程计划时刻、模板结束时刻与顺延量算出<b>有效结束时刻</b>。
    /// EndTime &lt;= 开始时刻的「时段」视为跨午夜，顺延到次日（与 RecurrenceExpander 跨午夜口径一致）；
    /// 再叠加 <paramref name="shiftMinutes"/>（整块同长顺移，结束与开始同时后移）。
    /// </summary>
    private static DateTime ComputeEndsAt(DateTime plannedAt, TimeOnly endTime, int shiftMinutes)
    {
        var endsAt = plannedAt.Date.Add(endTime.ToTimeSpan());
        if (endsAt <= plannedAt)
        {
            endsAt = endsAt.AddDays(1);
        }

        return endsAt.AddMinutes(shiftMinutes);
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
