using FuShi.Domain.Entities;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 引擎待处理的一条计划实例：Occurrence（账本行）+ 上屏/通知所需的科目上下文。
/// 由 <see cref="ScheduleService"/> 一次关联查询组装，避免引擎逐条回查数据库。
/// </summary>
/// <param name="Occurrence">账本行（含 Id / PlannedAt / 状态）。</param>
/// <param name="SubjectName">科目名（通知标题用）。</param>
/// <param name="Note">备注（可选）。</param>
public sealed record PendingReminder(Occurrence Occurrence, string SubjectName, string? Note);
