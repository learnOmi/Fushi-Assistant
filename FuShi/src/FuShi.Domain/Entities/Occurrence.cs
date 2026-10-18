using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 提醒实例：系统的核心「账本」（设计文档 2.3 ②）。
/// 它同时承担三件事——去重（已 Fired 不再弹）、补偿（对比 PlannedAt 与 now）、统计（完成/跳过率）。
/// 唯一键 = (SourceType, SourceId, PlannedAt)，在数据库层建唯一索引兜底并发/重放。
/// </summary>
public class Occurrence
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>来源类型（课表条目 / 事项）。</summary>
    public OwnerType SourceType { get; set; }

    /// <summary>来源主键（对应 ScheduleEntry.Id 或 TaskItem.Id）。</summary>
    public int SourceId { get; set; }

    /// <summary>
    /// 计划触发时刻（本地墙钟，Kind = Unspecified）。时区单独存于 AppSetting.TimezoneId，不存 UTC。
    /// </summary>
    public DateTime PlannedAt { get; set; }

    /// <summary>实际触发时刻；null 表示尚未触发。用于去重（不为 null 则不再弹）。</summary>
    public DateTime? FiredAt { get; set; }

    /// <summary>生命周期状态。</summary>
    public OccurrenceStatus Status { get; set; } = OccurrenceStatus.Pending;

    /// <summary>「稍后」到期时刻；Snoozed 状态时有值。</summary>
    public DateTime? SnoozeUntil { get; set; }
}
