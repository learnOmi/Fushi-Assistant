namespace FuShi.Infrastructure.Statistics;

/// <summary>
/// 科目维度的统计切片（M4b）：某科目在区间内的计划/实际时长与完成计数。
/// <para>
/// <see cref="SubjectId"/> 来自 <c>CompletionLog.SubjectId</c> 的<b>快照</b>，不是实时 join 条目；
/// 因此条目被删或改科目都不会改写历史。为空即「未分类」。
/// </para>
/// </summary>
/// <param name="SubjectId">科目主键；空表示未分类。</param>
/// <param name="SubjectName">科目名（快照查不到时退回「未分类」）。</param>
/// <param name="SubjectColor">科目色（十六进制），供横条着色。</param>
/// <param name="PlannedMinutes">计划总分钟数。</param>
/// <param name="ActualMinutes">实际总分钟数。</param>
/// <param name="DoneCount">完成次数。</param>
/// <param name="TotalCount">记录总次数（完成 + 跳过 + 错过）。</param>
/// <param name="SharePercent">占区间计划时长的百分比（0~100），用于横条宽度。</param>
public sealed record SubjectStatistics(
    int? SubjectId,
    string SubjectName,
    string SubjectColor,
    int PlannedMinutes,
    int ActualMinutes,
    int DoneCount,
    int TotalCount,
    double SharePercent);

/// <summary>
/// 单日完成情况（M4b）：用于「最近 7 天完成率」的柱状条。
/// <see cref="HasData"/> 为 false 表示当天没有任何完成记录，界面显示占位而不是 0%。
/// </summary>
/// <param name="Date">日期。</param>
/// <param name="DoneCount">完成次数。</param>
/// <param name="TotalCount">记录总次数。</param>
/// <param name="CompletionRate">完成率（0~100）；无记录时为 0。</param>
/// <param name="HasData">当天是否有完成记录。</param>
public sealed record DailyStatistics(
    DateOnly Date,
    int DoneCount,
    int TotalCount,
    double CompletionRate,
    bool HasData);

/// <summary>
/// 统计聚合结果（M4b）：一次区间查询的全部读数。
/// <para>
/// 口径统一在这里算好（完成率、占比），UI 只做文本与像素投影——
/// 避免同一百分比在服务与视图各算一遍而出现漂移。
/// </para>
/// </summary>
/// <param name="From">区间起（含）。</param>
/// <param name="To">区间止（含）。</param>
/// <param name="PlannedMinutes">计划总分钟数。</param>
/// <param name="ActualMinutes">实际总分钟数。</param>
/// <param name="DoneCount">完成次数。</param>
/// <param name="SkippedCount">跳过次数。</param>
/// <param name="MissedCount">错过次数。</param>
/// <param name="Subjects">按科目分布（按计划时长降序）。</param>
/// <param name="RecentDays">截至今天的最近 7 天（含无记录的日子）。</param>
public sealed record StatisticsReport(
    DateOnly From,
    DateOnly To,
    int PlannedMinutes,
    int ActualMinutes,
    int DoneCount,
    int SkippedCount,
    int MissedCount,
    IReadOnlyList<SubjectStatistics> Subjects,
    IReadOnlyList<DailyStatistics> RecentDays)
{
    /// <summary>记录总次数。</summary>
    public int TotalCount => DoneCount + SkippedCount + MissedCount;

    /// <summary>完成率（0~100）；无记录时为 0。</summary>
    public double CompletionRate => TotalCount == 0 ? 0 : DoneCount * 100.0 / TotalCount;

    /// <summary>区间内是否有任何完成记录（决定空态）。</summary>
    public bool HasData => TotalCount > 0;
}
