using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 提醒实例：系统的核心「账本」（设计文档 2.3 ②）。
/// 它同时承担三件事——去重（已 Fired 不再弹）、补偿（对比 PlannedAt 与 now）、统计（完成/跳过率）。
/// 唯一键 = (SourceType, SourceId, PlannedAt)，在数据库层建唯一索引兜底并发/重放。
/// <para>
/// 状态机（M3b/M3c）：<c>Pending → Fired → {InProgress → Done | 回退 Pending(顺延) | Skipped}</c>；
/// 用户「未响应」不改变状态（仍 Fired），只在 <c>CompletionLog</c> 记一条 Missed。
/// 「稍后」（M3c）不是"再提醒一次"：它把实例<b>回退成 Pending</b> 并叠加 <see cref="ShiftMinutes"/>（当日顺延调课），
/// 到新的有效触发点自然重弹；<b>不新建子实例</b>，也没有 SnoozeUntil 字段。
/// </para>
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

    /// <summary>
    /// 「当日顺延调课」的累计顺延分钟数（M3c）。默认 0。
    /// <para>
    /// <see cref="PlannedAt"/> 恒保持<b>模板时刻</b>不变；有效开始 = <c>PlannedAt.AddMinutes(ShiftMinutes)</c>，
    /// 有效结束 = 原结束时刻再 <c>+ ShiftMinutes</c>（整块同长顺移，总时长不变）。
    /// </para>
    /// <para>
    /// 之所以不直接改 <see cref="PlannedAt"/>：唯一键、幂等判重、对账谓词都基于 <c>PlannedAt</c>，
    /// 直接改会被 <c>MaterializeAsync</c>（补出重复课）与 <c>ReconcileAsync</c>（按模板重建、回滚顺延）自行回滚。
    /// </para>
    /// </summary>
    public int ShiftMinutes { get; set; }

    /// <summary>
    /// 顺延批次号（M3c）：一次「稍后」触发的整条链式顺延共用同一个批次号。
    /// 供对账（<c>ReconcileAsync</c>）按批次把<b>同批</b>实例一并回退（清零 <see cref="ShiftMinutes"/>、撤掉批次号），
    /// 消除"主角复原、配角还歪着"的残余错位。<c>null</c> = 未经顺延（或已回退）。
    /// </summary>
    public int? ShiftBatchId { get; set; }
}
