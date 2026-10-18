namespace FuShi.Domain.Scheduling;

/// <summary>
/// 时间线在某一时刻的「取景」结果：当前正在进行的时段、下一节、再下一节。
/// 三者都可能为 null——没有正在上的课、今天没有更多课、或未来已无排课。
/// </summary>
/// <param name="Current">当前正在进行（<c>StartsAt &lt;= now &lt; EndsAt</c>）的时段；无则 null。</param>
/// <param name="Next">当前时刻之后最近的一节；无则 null。</param>
/// <param name="Following">再下一节；无则 null。</param>
public sealed record TimelineView(
    TimelineSlot? Current,
    TimelineSlot? Next,
    TimelineSlot? Following);
