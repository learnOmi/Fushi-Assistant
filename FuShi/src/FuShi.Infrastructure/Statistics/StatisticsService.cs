using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Statistics;

/// <summary>
/// 统计服务（M4b）：按日期区间聚合 <see cref="CompletionLog"/>，供统计页展示。
/// <para>
/// <b>只读</b>：不写库、不缓存。个人数据量级下每次进页重算，比维护「何时失效」更可靠。
/// </para>
/// <para>
/// 科目归属只认 <c>CompletionLog.SubjectId</c> <b>快照</b>：历史记录不随条目删除或改科目而变化；
/// 快照为空或科目已不存在时统一归入「未分类」。
/// </para>
/// </summary>
public sealed class StatisticsService
{
    /// <summary>「最近 N 天」的窗口长度。</summary>
    public const int RECENT_DAYS = 7;

    /// <summary>未分类科目的显示名（快照为空或科目已被删除时使用）。</summary>
    internal const string UNCATEGORIZED_SUBJECT_NAME = "未分类";

    /// <summary>未分类科目的中性灰，避免借用某个真实科目的颜色。</summary>
    internal const string UNCATEGORIZED_SUBJECT_COLOR = "#8A8F98";

    /// <summary>百分比满值。</summary>
    private const double PERCENT_FULL = 100.0;

    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;

    /// <summary>构造统计服务。</summary>
    /// <param name="contextFactory">上下文工厂（每次查询创建短生命周期上下文）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public StatisticsService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// 聚合 <c>[from, to]</c>（双闭区间）内的完成记录。
    /// </summary>
    /// <param name="from">区间起（含）。</param>
    /// <param name="to">区间止（含）。</param>
    /// <param name="today">今天的日期（界定「最近 7 天」窗口；由调用方传入，避免服务内部读时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>统计聚合结果。</returns>
    /// <exception cref="ArgumentException">结束日期早于开始日期。</exception>
    public async Task<StatisticsReport> GetAsync(
        DateOnly from,
        DateOnly to,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("统计区间的结束日期不能早于开始日期。", nameof(to));
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await context.CompletionLogs
            .Where(log => log.Date >= from && log.Date <= to)
            .Select(log => new LogRow(log.Date, log.SubjectId, log.PlannedMin, log.ActualMin, log.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subjects = await LoadSubjectsAsync(context, rows, cancellationToken).ConfigureAwait(false);

        var report = new StatisticsReport(
            from,
            to,
            rows.Sum(static row => row.PlannedMinutes),
            rows.Sum(static row => row.ActualMinutes),
            rows.Count(static row => row.Status == CompletionStatus.Done),
            rows.Count(static row => row.Status == CompletionStatus.Skipped),
            rows.Count(static row => row.Status == CompletionStatus.Missed),
            BuildSubjectStatistics(rows, subjects),
            await BuildRecentDaysAsync(context, today, cancellationToken).ConfigureAwait(false));

        _logger.Debug(
            "统计聚合：{From}~{To} 共 {Count} 条记录，完成 {Done} 条",
            from,
            to,
            report.TotalCount,
            report.DoneCount);

        return report;
    }

    /// <summary>按记录里出现过的科目主键批量取科目（已删除的不会返回）。</summary>
    private static async Task<Dictionary<int, Subject>> LoadSubjectsAsync(
        FuShiDbContext context,
        IReadOnlyList<LogRow> rows,
        CancellationToken cancellationToken)
    {
        var ids = rows
            .Where(static row => row.SubjectId.HasValue)
            .Select(static row => row.SubjectId!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, Subject>();
        }

        return await context.Subjects
            .Where(subject => ids.Contains(subject.Id))
            .ToDictionaryAsync(subject => subject.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 组装科目分布：按计划时长降序，占比优先按计划时长算；
    /// 若整个区间计划时长为 0（旧数据缺模板），退化为按记录条数算，保证横条仍有意义。
    /// <para>
    /// 分组键取<b>解析后的科目</b>：快照为空与「科目已被删除」都落到同一个「未分类」桶，
    /// 避免界面上出现多行同名项。
    /// </para>
    /// </summary>
    private static IReadOnlyList<SubjectStatistics> BuildSubjectStatistics(
        IReadOnlyList<LogRow> rows,
        IReadOnlyDictionary<int, Subject> subjects)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<SubjectStatistics>();
        }

        var totalPlanned = rows.Sum(static row => row.PlannedMinutes);
        var totalRows = rows.Count;

        return rows
            .GroupBy(row => ResolveSubjectId(row, subjects))
            .Select(group =>
            {
                var planned = group.Sum(static row => row.PlannedMinutes);
                var share = totalPlanned > 0
                    ? planned * PERCENT_FULL / totalPlanned
                    : group.Count() * PERCENT_FULL / totalRows;

                Subject? subject = null;
                if (group.Key.HasValue)
                {
                    subjects.TryGetValue(group.Key.Value, out subject);
                }

                return new SubjectStatistics(
                    group.Key,
                    subject?.Name ?? UNCATEGORIZED_SUBJECT_NAME,
                    subject?.Color ?? UNCATEGORIZED_SUBJECT_COLOR,
                    planned,
                    group.Sum(static row => row.ActualMinutes),
                    group.Count(static row => row.Status == CompletionStatus.Done),
                    group.Count(),
                    share);
            })
            .OrderByDescending(static subject => subject.PlannedMinutes)
            .ThenByDescending(static subject => subject.TotalCount)
            .ThenBy(static subject => subject.SubjectName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>把记录里的科目主键解析成统计分组键；快照为空或科目已删除一律返回 null。</summary>
    /// <param name="row">完成记录行。</param>
    /// <param name="subjects">本次查询到的科目字典。</param>
    private static int? ResolveSubjectId(LogRow row, IReadOnlyDictionary<int, Subject> subjects)
        => row.SubjectId is { } id && subjects.ContainsKey(id) ? id : null;

    /// <summary>
    /// 组装最近 <see cref="RECENT_DAYS"/> 天：窗口固定为「今天往前 6 天 ~ 今天」，
    /// 无记录的日子也保留一行（界面显示占位），避免柱子数量随数据跳动。
    /// </summary>
    private static async Task<IReadOnlyList<DailyStatistics>> BuildRecentDaysAsync(
        FuShiDbContext context,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var recentFrom = today.AddDays(-(RECENT_DAYS - 1));

        var recentRows = await context.CompletionLogs
            .Where(log => log.Date >= recentFrom && log.Date <= today)
            .Select(log => new { log.Date, log.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byDate = recentRows
            .GroupBy(static row => row.Date)
            .ToDictionary(static group => group.Key, static group => group.ToList());

        var days = new List<DailyStatistics>(RECENT_DAYS);
        for (var offset = 0; offset < RECENT_DAYS; offset++)
        {
            var date = recentFrom.AddDays(offset);
            var hasData = byDate.TryGetValue(date, out var dayRows);
            var total = hasData ? dayRows!.Count : 0;
            var done = hasData ? dayRows!.Count(static row => row.Status == CompletionStatus.Done) : 0;

            days.Add(new DailyStatistics(
                date,
                done,
                total,
                total == 0 ? 0 : done * PERCENT_FULL / total,
                hasData));
        }

        return days;
    }

    /// <summary>查询行：只取聚合需要的列，不把整行实体读进内存。</summary>
    private sealed record LogRow(DateOnly Date, int? SubjectId, int PlannedMinutes, int ActualMinutes, CompletionStatus Status);
}
