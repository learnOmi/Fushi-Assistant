using FuShi.Domain.Entities;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 引擎待处理的一条计划实例：Occurrence（账本行）+ 上屏/通知所需的上下文。
/// 由 <see cref="ScheduleService"/> 一次关联查询组装，避免引擎逐条回查数据库。
/// 课表条目与事项（M4c）共用本模型：来源类型由 <see cref="IsTask"/> 区分。
/// </summary>
/// <param name="Occurrence">账本行（含 Id / PlannedAt / 状态）。PlannedAt 是「计划开始（或截止）时刻」，仅用于展示。</param>
/// <param name="SubjectName">标题：课表取科目名，事项取事项标题（通知标题用）。</param>
/// <param name="Note">备注（可选）。</param>
/// <param name="TriggerAt">
/// 实际触发时刻（M3c）：<c>PlannedAt + 顺延量 − 提前量</c>（顺延量 = <c>Occurrence.ShiftMinutes</c>，0 表示未顺延）。
/// 引擎用它算休眠时长与补偿窗口，展示仍用 <see cref="Occurrence"/> 的 PlannedAt。
/// </param>
/// <param name="EndsAt">
/// 结束时刻（本地墙钟）。课表由模板 <c>EndTime</c> 与 PlannedAt 的日期算出，跨午夜顺延次日；
/// 事项没有时长（M4c），恒等于 <c>PlannedAt + 顺延量</c>。
/// 用于弹窗展示与「进行中」到点自动完成（事项不参与）。
/// </param>
/// <param name="LeadMinutes">
/// 本条实例实际采用的提前量（分钟，M4c）：优先取物主自己的 <c>ReminderRule</c>，无规则时回落全局。
/// 补偿判决必须用它逐条计算触发点，不能用全局值——否则规则提前量不同会让判决口径漂移。
/// </param>
/// <param name="IsTask">是否来自事项；决定通知正文用「截止」还是「开始」。</param>
public sealed record PendingReminder(
    Occurrence Occurrence,
    string SubjectName,
    string? Note,
    DateTime TriggerAt,
    DateTime EndsAt,
    int LeadMinutes,
    bool IsTask);
