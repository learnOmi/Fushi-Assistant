using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;

namespace FuShi.Domain.Dedup;

/// <summary>
/// Occurrence 的业务唯一键 = (SourceType, SourceId, PlannedAt)。
/// 与数据库唯一索引一一对应：算法层先在内存去重（省往返），唯一索引在库层兜底（并发/重放）。
/// </summary>
/// <param name="SourceType">来源类型。</param>
/// <param name="SourceId">来源主键。</param>
/// <param name="PlannedAt">计划时刻（本地墙钟）。</param>
public readonly record struct OccurrenceKey(OwnerType SourceType, int SourceId, DateTime PlannedAt)
{
    /// <summary>由计划实例构造键。</summary>
    public static OccurrenceKey From(PlannedOccurrence occurrence) =>
        new(occurrence.SourceType, occurrence.SourceId, occurrence.PlannedAt);
}
