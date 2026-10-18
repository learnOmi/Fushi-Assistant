namespace FuShi.Domain.Scheduling;

/// <summary>
/// 时间线上的一条时段（已由周期模板展开成具体日期，不是「周几」模板）。
/// 它同时服务两个消费方：调度引擎（按 StartsAt 排队触发）与悬浮组件上屏（展示科目/倒计时/进度）。
/// 科目名与颜色随行携带，使上层无需再做一次关联查询即可直接展示。
/// </summary>
/// <param name="StartsAt">开始时刻（本地墙钟，Kind = Unspecified）。</param>
/// <param name="EndsAt">结束时刻；跨午夜条目为次日。</param>
/// <param name="SubjectId">科目主键。</param>
/// <param name="SubjectName">科目名（上屏用）。</param>
/// <param name="SubjectColor">科目颜色（#RRGGBB，上屏用）。</param>
/// <param name="Note">备注（可选）。</param>
public sealed record TimelineSlot(
    DateTime StartsAt,
    DateTime EndsAt,
    int SubjectId,
    string SubjectName,
    string SubjectColor,
    string? Note);
