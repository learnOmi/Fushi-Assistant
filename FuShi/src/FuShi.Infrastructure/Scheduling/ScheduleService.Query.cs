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
/// 课表服务的<b>查询与状态写入</b>部分（M5c 拆分，纯移动不改行为）：
/// 供 UI 取网格/时间线，供调度引擎取到期与未来待触发实例，以及把实例标记为已触发 / 已错过。
/// </summary>
public sealed partial class ScheduleService
{
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
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // M4c 起提前量可能逐物主不同（事项可挂 ReminderRule），SQL 窗口必须按「最大提前量」放宽，
        // 再在内存里用每一行自己的提前量精筛，否则提前量大的实例会漏出窗口。
        var leads = await LoadLeadContextAsync(context, cancellationToken).ConfigureAwait(false);

        // 有效触发点 ∈ [since, now]。因 ShiftMinutes 会把触发点往后移，SQL 下界放宽一个常量（内存再精筛）。
        var pendingLower = since.AddMinutes(leads.MaxLeadMinutes - TimeDefaults.SHIFT_WINDOW_SLACK_MINUTES);
        var pendingUpper = now.AddMinutes(leads.MaxLeadMinutes);

        var rows = await SelectPendingRowsAsync(context, pendingLower, pendingUpper, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => Map(row, leads))
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
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var leads = await LoadLeadContextAsync(context, cancellationToken).ConfigureAwait(false);

        // 有效触发点 > now；因 ShiftMinutes 会把触发点往后移，SQL 下界放宽一个常量（内存再精筛）。
        var pendingCutoff = now.AddMinutes(leads.MaxLeadMinutes - TimeDefaults.SHIFT_WINDOW_SLACK_MINUTES);

        var rows = await SelectUpcomingRowsAsync(context, pendingCutoff, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => Map(row, leads))
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
    /// 载入提前量上下文：全局 <c>LeadTimeMinutes</c> + 全部 <c>ReminderRule</c>。
    /// 规则按物主分组后交给 <see cref="ReminderRuleResolver"/> 解析，口径唯一。
    /// </summary>
    private async Task<LeadContext> LoadLeadContextAsync(
        FuShiDbContext context,
        CancellationToken cancellationToken)
    {
        var globalLead = await GetLeadMinutesAsync(cancellationToken).ConfigureAwait(false);
        var rules = await context.ReminderRules
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byOwner = rules
            .GroupBy(static rule => (rule.OwnerType, rule.OwnerId))
            .ToDictionary(
                static group => group.Key,
                group => ReminderRuleResolver.ResolveLeadMinutes(group, globalLead));

        return new LeadContext(
            globalLead,
            ReminderRuleResolver.ResolveMaxLeadMinutes(rules, globalLead),
            byOwner);
    }

    /// <summary>
    /// 关联查询「本轮到期」的候选行：课表实例（Occurrence ⋈ ScheduleEntry ⋈ Subject）
    /// 与事项实例（Occurrence ⋈ TaskItem）两路合并。
    /// 谓词见 <see cref="GetDueAsync"/> 注释：待触发实例按计划时刻落在放宽后的窗口内（有效触发点由内存精筛）。
    /// 注意：参数不取名 from/to——`from` 是查询表达式的上下文关键字，会与 `from ... in` 冲突。
    /// </summary>
    private static async Task<IReadOnlyList<PendingRow>> SelectPendingRowsAsync(
        FuShiDbContext context,
        DateTime pendingLower,
        DateTime pendingUpper,
        CancellationToken cancellationToken)
    {
        var scheduleRows = await (from occurrence in context.Occurrences
                                  join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                                  join subject in context.Subjects on entry.SubjectId equals subject.Id
                                  where occurrence.SourceType == OwnerType.ScheduleEntry
                                        && occurrence.Status == OccurrenceStatus.Pending
                                        && occurrence.FiredAt == null
                                        && occurrence.PlannedAt >= pendingLower
                                        && occurrence.PlannedAt <= pendingUpper
                                  select new PendingRow(occurrence, subject.Name, entry.Note, entry.EndTime, false))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskRows = await (from occurrence in context.Occurrences
                              join task in context.Tasks on occurrence.SourceId equals task.Id
                              where occurrence.SourceType == OwnerType.Task
                                    && occurrence.Status == OccurrenceStatus.Pending
                                    && occurrence.FiredAt == null
                                    && occurrence.PlannedAt >= pendingLower
                                    && occurrence.PlannedAt <= pendingUpper
                              select new PendingRow(occurrence, task.Title, null, null, true))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return scheduleRows.Concat(taskRows).ToList();
    }

    /// <summary>
    /// 关联查询「未来待触发」的候选行（课表 + 事项）：待触发实例计划时刻在 <paramref name="pendingCutoff"/> 之后
    /// （有效触发点由内存精筛）。
    /// </summary>
    private static async Task<IReadOnlyList<PendingRow>> SelectUpcomingRowsAsync(
        FuShiDbContext context,
        DateTime pendingCutoff,
        CancellationToken cancellationToken)
    {
        var scheduleRows = await (from occurrence in context.Occurrences
                                  join entry in context.ScheduleEntries on occurrence.SourceId equals entry.Id
                                  join subject in context.Subjects on entry.SubjectId equals subject.Id
                                  where occurrence.SourceType == OwnerType.ScheduleEntry
                                        && occurrence.Status == OccurrenceStatus.Pending
                                        && occurrence.FiredAt == null
                                        && occurrence.PlannedAt > pendingCutoff
                                  select new PendingRow(occurrence, subject.Name, entry.Note, entry.EndTime, false))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskRows = await (from occurrence in context.Occurrences
                              join task in context.Tasks on occurrence.SourceId equals task.Id
                              where occurrence.SourceType == OwnerType.Task
                                    && occurrence.Status == OccurrenceStatus.Pending
                                    && occurrence.FiredAt == null
                                    && occurrence.PlannedAt > pendingCutoff
                              select new PendingRow(occurrence, task.Title, null, null, true))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return scheduleRows.Concat(taskRows).ToList();
    }

    /// <summary>把查询行组装成引擎用的计划实例，并算出有效触发点与有效结束时刻。</summary>
    private static PendingReminder Map(PendingRow row, LeadContext leads)
    {
        var occurrence = row.Occurrence;
        var leadMinutes = leads.Resolve(occurrence.SourceType, occurrence.SourceId);

        // 事项没有时长（M4c）：结束时刻就等于顺延后的截止时刻，不参与「进行中」自动完成。
        var endsAt = row.IsTask
            ? occurrence.PlannedAt.AddMinutes(occurrence.ShiftMinutes)
            : ComputeEndsAt(occurrence.PlannedAt, row.EndTime ?? TimeOnly.MinValue, occurrence.ShiftMinutes);

        return new PendingReminder(
            occurrence,
            row.Title,
            row.Note,
            EffectiveTriggerAt(occurrence, leadMinutes),
            endsAt,
            leadMinutes,
            row.IsTask);
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
}
