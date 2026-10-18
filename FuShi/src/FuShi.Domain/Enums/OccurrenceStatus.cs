namespace FuShi.Domain.Enums;

/// <summary>
/// 提醒实例的生命周期状态（设计文档 2.1 Occurrence.status）。
/// 状态机：Pending →（Fired / Snoozed / Skipped / Done / Missed）；
/// Snoozed 到期后回到 Pending 或直接 Fired；Missed 是补偿扫描的终态。
/// </summary>
public enum OccurrenceStatus
{
    /// <summary>待触发：已排入调度、等待到点。</summary>
    Pending = 0,

    /// <summary>已触发：已弹过提醒（FiredAt 有值），用于去重。</summary>
    Fired = 1,

    /// <summary>已延后：用户点了「稍后」，SnoozeUntil 到期后重新参与调度。</summary>
    Snoozed = 2,

    /// <summary>已跳过：用户主动跳过，不再提醒，但仍计入统计。</summary>
    Skipped = 3,

    /// <summary>已完成：用户点了「开始」并完成，写入 CompletionLog。</summary>
    Done = 4,

    /// <summary>已错过：超过宽限期仍未触发，被补偿扫描判定为错过（只汇总，不逐条弹）。</summary>
    Missed = 5,
}
