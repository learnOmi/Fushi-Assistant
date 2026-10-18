namespace FuShi.Domain.Recurrence;

/// <summary>
/// 周期展开的结果：展开出的计划实例 + 过程中发生的 DST 调整明细。
/// 把「调整明细」作为一等输出，保证调用方必须处理（记录日志），而不是被吞掉。
/// </summary>
/// <param name="Occurrences">展开得到的计划实例集合（按时间升序）。</param>
/// <param name="Adjustments">DST 调整明细。</param>
public sealed record ExpansionResult(
    IReadOnlyList<PlannedOccurrence> Occurrences,
    IReadOnlyList<DstAdjustment> Adjustments);
