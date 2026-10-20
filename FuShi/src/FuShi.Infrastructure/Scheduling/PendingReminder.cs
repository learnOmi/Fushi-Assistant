using FuShi.Domain.Entities;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 引擎待处理的一条计划实例：Occurrence（账本行）+ 上屏/通知所需的科目上下文。
/// 由 <see cref="ScheduleService"/> 一次关联查询组装，避免引擎逐条回查数据库。
/// </summary>
/// <param name="Occurrence">账本行（含 Id / PlannedAt / 状态）。PlannedAt 是「课程开始时刻」，仅用于展示。</param>
/// <param name="SubjectName">科目名（通知标题用）。</param>
/// <param name="Note">备注（可选）。</param>
/// <param name="TriggerAt">
/// 实际触发时刻（M3c）：<c>PlannedAt + 顺延量 − 提前量</c>（顺延量 = <c>Occurrence.ShiftMinutes</c>，0 表示未顺延）。
/// 引擎用它算休眠时长与补偿窗口，展示仍用 <see cref="Occurrence"/> 的 PlannedAt。
/// </param>
/// <param name="EndsAt">
/// 课程结束时刻（本地墙钟）。由模板的 <c>EndTime</c> 与 PlannedAt 的日期算出，跨午夜顺延次日。
/// 用于弹窗展示与「进行中」到点自动完成。
/// </param>
public sealed record PendingReminder(
    Occurrence Occurrence,
    string SubjectName,
    string? Note,
    DateTime TriggerAt,
    DateTime EndsAt);
