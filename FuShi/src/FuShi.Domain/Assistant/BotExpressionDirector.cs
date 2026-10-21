using FuShi.Domain.Enums;

namespace FuShi.Domain.Assistant;

/// <summary>
/// 课程/交互信号 → 稳态表情的纯函数。
/// <para>
/// 只回答「现在应该长期显示哪张脸」，不处理瞬态插播（blink/wander 由 UI 自主播放，
/// laugh/surprise 由视图模型检测上升沿后发出），也不做任何 IO。
/// 优先级固定，顺序本身就是产品规则，改动必须同步设计稿第 03 节的接入表。
/// </para>
/// </summary>
public static class BotExpressionDirector
{
    /// <summary>
    /// 解析当前应长期显示的情绪。
    /// <list type="number">
    /// <item>在课上：已完成 → Done；逾期未响应（Fired / Missed）→ Late；其余 → Focus。</item>
    /// <item>不在课上但下一节进入提前量窗口 → Soon（提前量为 0 时本档关闭，与提醒语义一致）。</item>
    /// <item>刚下课且今天还有后续 → Break。</item>
    /// <item>长期未交互 → Sleepy。</item>
    /// <item>其余 → Idle。</item>
    /// </list>
    /// 「在课上却 2 小时没交互」仍返回 Focus：困倦是待机氛围，不能覆盖正在发生的课程。
    /// </summary>
    /// <param name="signals">当前事实。</param>
    /// <param name="leadMinutes">提前提醒分钟数（来自设置）；0 表示不提前，故不出现 Soon。</param>
    /// <returns>稳态情绪。</returns>
    public static BotExpression ResolveSteady(BotSignals signals, int leadMinutes)
    {
        if (signals.InClass)
        {
            return signals.CurrentStatus switch
            {
                OccurrenceStatus.Done => BotExpression.Done,
                OccurrenceStatus.Missed => BotExpression.Late,
                OccurrenceStatus.Fired => BotExpression.Late, // 已到开课时刻仍未点「开始」= 打卡逾期。
                _ => BotExpression.Focus,
            };
        }

        if (leadMinutes > 0
            && signals.MinutesUntilNext is int minutesUntilNext
            && minutesUntilNext <= leadMinutes)
        {
            return BotExpression.Soon;
        }

        if (signals.HasFollowingToday
            && signals.MinutesSinceLastEnd is int minutesSinceLastEnd
            && minutesSinceLastEnd >= 0
            && minutesSinceLastEnd <= BotExpressionCatalog.BREAK_WINDOW_MINUTES)
        {
            return BotExpression.Break;
        }

        if (signals.IdleFor >= BotExpressionCatalog.SLEEPY_AFTER)
        {
            return BotExpression.Sleepy;
        }

        return BotExpression.Idle;
    }
}
