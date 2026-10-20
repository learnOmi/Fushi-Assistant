namespace FuShi.Domain.Enums;

/// <summary>
/// 提醒实例的生命周期状态（设计文档 2.1 Occurrence.status）。
/// <para>
/// M3b 起状态机细化为（另见 <c>Occurrence</c> 注释）。M3c 起「稍后」不再是状态，而是<b>当日顺延</b>
/// （回退到 Pending + 叠 <c>ShiftMinutes</c>），故 Snoozed 已移除：
/// <code>
/// Pending ──引擎触发──▶ Fired
/// Fired ─┬─「开始」────▶ InProgress ──(到 EndsAt / 手点完成)──▶ Done
///        ├─「稍后」────▶ 回退 Pending（叠 ShiftMinutes，到新触发点重弹）
///        ├─「跳过」────▶ Skipped
///        └─「未响应」──▶ 仍 Fired，另写 CompletionLog(Missed)
/// </code>
/// Missed 是补偿扫描的终态。
/// </para>
/// <para>
/// 取值 2 原为 <c>Snoozed</c>，M3c 已废弃；为不打乱既有数据的语义，<b>不重排后续取值</b>（保留空位）。
/// </para>
/// </summary>
public enum OccurrenceStatus
{
    /// <summary>待触发：已排入调度、等待到点。</summary>
    Pending = 0,

    /// <summary>已触发：已弹过提醒（FiredAt 有值），用于去重。</summary>
    Fired = 1,

    // 取值 2 曾为 Snoozed（M3c 移除，保留空位以免既有数据语义错位）。

    /// <summary>已跳过：用户主动跳过，不再提醒，但仍计入统计。</summary>
    Skipped = 3,

    /// <summary>已完成：用户点了「开始」并完成，写入 CompletionLog。</summary>
    Done = 4,

    /// <summary>已错过：超过宽限期仍未触发，被补偿扫描判定为错过（只汇总，不逐条弹）。</summary>
    Missed = 5,

    /// <summary>
    /// 进行中：用户点了「开始」正在执行（M3b 新增）。
    /// <b>必须追加在末尾</b>（值 6），避免重排既有值导致库中旧数据语义错位。
    /// 开始时刻借用 <c>Occurrence.FiredAt</c> 记录；到 EndsAt 或用户点「完成」时转 <see cref="Done"/>。
    /// </summary>
    InProgress = 6,
}
