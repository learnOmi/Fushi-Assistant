using FuShi.Domain.Enums;

namespace FuShi.Domain.Scheduling;

/// <summary>
/// 时间线上的一条时段（已由周期模板展开成具体日期，不是「周几」模板）。
/// 它同时服务两个消费方：调度引擎（按 StartsAt 排队触发）与悬浮组件上屏（展示科目/倒计时/进度）。
/// 科目名与颜色随行携带，使上层无需再做一次关联查询即可直接展示。
/// <para>
/// 末尾两列是「账本状态投影」（M3b 可见反馈）：由课表服务把对应 <c>Occurrence</c> 的当前状态与顺延量一并带上，
/// 使悬浮组件能展示「已跳过 / 已顺延」等结果。模板展开本身不产生状态，故 <see cref="Status"/> 可空——
/// null 表示该时段尚无账本行。
/// </para>
/// </summary>
/// <param name="StartsAt">开始时刻（本地墙钟，Kind = Unspecified）；已叠加顺延量，即「有效开始」。</param>
/// <param name="EndsAt">结束时刻；跨午夜条目为次日；已叠加顺延量。</param>
/// <param name="SubjectId">科目主键。</param>
/// <param name="SubjectName">科目名（上屏用）。</param>
/// <param name="SubjectColor">科目颜色（#RRGGBB，上屏用）。</param>
/// <param name="Note">备注（可选）。</param>
/// <param name="Status">该时段对应的账本状态；null = 尚无账本行。</param>
/// <param name="ShiftMinutes">该时段被「当日顺延」累计的分钟数（M3c）；0 = 未顺延。供悬浮组件显示「已顺延」徽标。</param>
public sealed record TimelineSlot(
    DateTime StartsAt,
    DateTime EndsAt,
    int SubjectId,
    string SubjectName,
    string SubjectColor,
    string? Note,
    OccurrenceStatus? Status = null,
    int ShiftMinutes = 0);
