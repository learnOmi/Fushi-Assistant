using FuShi.Domain.Enums;

namespace FuShi.Domain.Recurrence;

/// <summary>
/// 由周期模板展开出的「计划提醒实例」。
/// 这是纯计算产物（未落库），落库后成为 Occurrence 实体。
/// </summary>
/// <param name="SourceType">来源类型（课表条目）。</param>
/// <param name="SourceId">来源主键（ScheduleEntry.Id）。</param>
/// <param name="PlannedAt">计划开始时刻（本地墙钟，Kind = Unspecified）。</param>
/// <param name="EndsAt">计划结束时刻；跨午夜条目为次日。用于进度条与完成统计。</param>
/// <param name="SubjectId">科目主键，用于上屏取色与名称。</param>
/// <param name="Note">备注（可选）。</param>
public sealed record PlannedOccurrence(
    OwnerType SourceType,
    int SourceId,
    DateTime PlannedAt,
    DateTime EndsAt,
    int SubjectId,
    string? Note);
