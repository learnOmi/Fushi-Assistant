using FuShi.Domain.Enums;
using FuShi.Domain.Scheduling;

namespace FuShi.Domain.Assistant;

/// <summary>
/// 从已展开的时段表与「最后一次交互时刻」提炼 <see cref="BotSignals"/>（M4.5d）。
/// <para>
/// 纯取数：不读系统时钟、不碰数据库，<c>now</c> 与交互时刻都由调用方传入，
/// 因此可以用固定时刻单测。所有口径与设计稿第 03 节接入表一致：
/// 「临期」按分钟向上取整（4 分 30 秒算 5 分钟），「刚下课」按分钟向下取整。
/// </para>
/// </summary>
public static class BotSignalBuilder
{
    /// <summary>判定「整日全部完成」所需的最少课程数，避免只有一两节课时也弹大笑。</summary>
    private const int MIN_SLOTS_FOR_ALL_DONE = 3;

    /// <summary>
    /// 构造表情状态机的输入。
    /// </summary>
    /// <param name="slots">已展开的时段表（升序）；允许为空。</param>
    /// <param name="now">当前时刻（本地墙钟）。</param>
    /// <param name="lastInteractionAt">最后一次用户交互时刻；用于计算 <see cref="BotSignals.IdleFor"/>。</param>
    /// <param name="allDoneForSevenDays">连续 7 天全勤（依赖统计能力，M4b 之前恒为 false）。</param>
    public static BotSignals Build(
        IReadOnlyList<TimelineSlot> slots,
        DateTime now,
        DateTime lastInteractionAt,
        bool allDoneForSevenDays = false)
    {
        var safeSlots = slots ?? Array.Empty<TimelineSlot>();
        var today = DateOnly.FromDateTime(now);

        TimelineSlot? current = null;
        TimelineSlot? next = null;
        TimelineSlot? lastEndedToday = null;
        var todaySlots = new List<TimelineSlot>();

        foreach (var slot in safeSlots)
        {
            if (DateOnly.FromDateTime(slot.StartsAt) == today)
            {
                todaySlots.Add(slot);
            }

            if (current is null && slot.StartsAt <= now && now < slot.EndsAt)
            {
                current = slot;
            }

            // 已跳过的课不参与「下一节」：与悬浮组件「已跳过不算下一节」的口径一致。
            if (slot.StartsAt > now
                && slot.Status != OccurrenceStatus.Skipped
                && (next is null || slot.StartsAt < next.StartsAt))
            {
                next = slot;
            }

            if (slot.EndsAt <= now
                && slot.Status != OccurrenceStatus.Skipped
                && DateOnly.FromDateTime(slot.EndsAt) == today
                && (lastEndedToday is null || slot.EndsAt > lastEndedToday.EndsAt))
            {
                lastEndedToday = slot;
            }
        }

        var hasFollowingToday = todaySlots.Exists(
            slot => slot.StartsAt > now && slot.Status != OccurrenceStatus.Skipped);

        var idleFor = now - lastInteractionAt;
        if (idleFor < TimeSpan.Zero)
        {
            idleFor = TimeSpan.Zero;
        }

        return new BotSignals(
            InClass: current is not null,
            CurrentStatus: current?.Status,
            MinutesUntilNext: next is null
                ? null
                : (int)Math.Ceiling((next.StartsAt - now).TotalMinutes),
            MinutesSinceLastEnd: lastEndedToday is null
                ? null
                : Math.Max(0, (int)Math.Floor((now - lastEndedToday.EndsAt).TotalMinutes)),
            HasFollowingToday: hasFollowingToday,
            HasConflict: HasOverlap(todaySlots, now),
            AllDoneToday: IsAllDone(todaySlots),
            AllDoneForSevenDays: allDoneForSevenDays,
            IdleFor: idleFor);
    }

    /// <summary>
    /// 今天是否存在时间重叠的课：按开始时刻排序后，只要有一节的开始早于「此前最晚结束」，
    /// 就说明它与前面某节压在一起。已结束的课不再计入——晚上启动不该为早上的冲突弹惊讶；
    /// 被跳过的课也不算冲突。跨午夜条目只按开始日归类，不拆分成两条。
    /// </summary>
    /// <param name="todaySlots">今天的时段。</param>
    /// <param name="now">当前时刻，用于剔除已结束的课。</param>
    private static bool HasOverlap(List<TimelineSlot> todaySlots, DateTime now)
    {
        var ordered = todaySlots
            .Where(slot => slot.Status != OccurrenceStatus.Skipped && slot.EndsAt > now)
            .OrderBy(slot => slot.StartsAt)
            .ToList();

        DateTime? latestEnd = null;
        foreach (var slot in ordered)
        {
            if (latestEnd is not null && slot.StartsAt < latestEnd.Value)
            {
                return true;
            }

            if (latestEnd is null || slot.EndsAt > latestEnd.Value)
            {
                latestEnd = slot.EndsAt;
            }
        }

        return false;
    }

    /// <summary>今天的课是否全部结课：至少三节，且每一节都已是 <see cref="OccurrenceStatus.Done"/>。</summary>
    /// <param name="todaySlots">今天的时段。</param>
    private static bool IsAllDone(List<TimelineSlot> todaySlots) =>
        todaySlots.Count >= MIN_SLOTS_FOR_ALL_DONE
        && todaySlots.TrueForAll(slot => slot.Status == OccurrenceStatus.Done);
}
