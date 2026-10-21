using FuShi.Domain.Enums;

namespace FuShi.Domain.Assistant;

/// <summary>
/// 表情状态机的输入：<b>只承载事实，不承载判断</b>。
/// <para>
/// 由悬浮组件的视图模型在每秒刷新时构造：课程相关的字段取自已展开的时段表，
/// <see cref="IdleFor"/> 取自最后一次用户交互时刻。状态机本身不读时钟、不碰数据库，
/// 因此可以完全用固定值单测。
/// </para>
/// </summary>
/// <param name="InClass">此刻是否正在上课（时段表里存在 <c>StartsAt ≤ now &lt; EndsAt</c> 的条目）。</param>
/// <param name="CurrentStatus">当前时段的账本状态；null = 尚无账本行（视为正常上课）。</param>
/// <param name="MinutesUntilNext">距下一节开始还有多少分钟；没有下一节时为 null。</param>
/// <param name="MinutesSinceLastEnd">距上一节结束过了多少分钟；今天还没上过课时为 null。</param>
/// <param name="HasFollowingToday">今天是否还有未跳过的后续课程（用于区分「课间」与「今天上完了」）。</param>
/// <param name="HasConflict">今天是否存在时间重叠的课程（驱动一次惊讶瞬态，不影响稳态）。</param>
/// <param name="AllDoneToday">今天的课程是否全部完成（驱动一次大笑瞬态，不影响稳态）。</param>
/// <param name="AllDoneForSevenDays">是否连续 7 天全勤（彩蛋，依赖统计能力）。</param>
/// <param name="IdleFor">距最后一次用户交互过了多久。</param>
public readonly record struct BotSignals(
    bool InClass,
    OccurrenceStatus? CurrentStatus,
    int? MinutesUntilNext,
    int? MinutesSinceLastEnd,
    bool HasFollowingToday,
    bool HasConflict,
    bool AllDoneToday,
    bool AllDoneForSevenDays,
    TimeSpan IdleFor);
