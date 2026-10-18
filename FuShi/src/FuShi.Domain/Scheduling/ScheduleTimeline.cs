namespace FuShi.Domain.Scheduling;

/// <summary>
/// 时间线取景：给定「按开始时刻升序排列」的时段列表与当前时刻，挑出当前节 / 下一节 / 再下一节，
/// 并给出当前节的完成进度。悬浮组件四要素（科目 / 倒计时 / 进度条 / 下一节）的判定就落在这里。
/// 纯算法——now 由调用方传入，不读系统时钟、不碰数据库，因此可 100% 单测。
/// </summary>
public static class ScheduleTimeline
{
    /// <summary>进度百分比的下界（%）——归一化常量，避免在公式里出现裸数字。</summary>
    private const double MIN_PERCENT = 0.0;

    /// <summary>进度百分比的上界（%）——当前节结束后一律封顶。</summary>
    private const double MAX_PERCENT = 100.0;

    /// <summary>
    /// 取景。
    /// 规则：<c>StartsAt &lt;= now &lt; EndsAt</c> 视为「当前」；<c>StartsAt &gt; now</c> 视为「未来」，
    /// 未来项按顺序取前两个作为「下一节 / 再下一节」。假定 <paramref name="slots"/> 已升序。
    /// </summary>
    /// <param name="slots">升序的时段列表。</param>
    /// <param name="now">当前时刻（本地墙钟）。</param>
    public static TimelineView Select(IReadOnlyList<TimelineSlot> slots, DateTime now)
    {
        TimelineSlot? current = null;
        TimelineSlot? next = null;
        TimelineSlot? following = null;

        foreach (var slot in slots)
        {
            if (current is null && slot.StartsAt <= now && now < slot.EndsAt)
            {
                current = slot;
                continue;
            }

            if (slot.StartsAt > now)
            {
                if (next is null)
                {
                    next = slot;
                }
                else
                {
                    following = slot;
                    break; // 未来项只需前两个，后续无需再看。
                }
            }
        }

        return new TimelineView(current, next, following);
    }

    /// <summary>
    /// 当前节的完成进度（0~100）。用于悬浮组件的进度条。
    /// 时长非正、或时刻落在时段之外时，一律夹到 [0, 100]，避免出现负宽或超长填充。
    /// </summary>
    /// <param name="slot">当前时段。</param>
    /// <param name="now">当前时刻。</param>
    public static double GetProgressPercent(TimelineSlot slot, DateTime now)
    {
        var totalSeconds = (slot.EndsAt - slot.StartsAt).TotalSeconds;
        if (totalSeconds <= 0)
        {
            return MIN_PERCENT;
        }

        var elapsedSeconds = (now - slot.StartsAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return MIN_PERCENT;
        }

        if (elapsedSeconds >= totalSeconds)
        {
            return MAX_PERCENT;
        }

        return elapsedSeconds / totalSeconds * MAX_PERCENT;
    }
}
