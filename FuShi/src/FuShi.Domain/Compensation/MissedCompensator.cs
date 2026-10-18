using FuShi.Domain.Entities;
using FuShi.Domain.Enums;

namespace FuShi.Domain.Compensation;

/// <summary>
/// 错过补偿：把「已过期但尚未处理」的计划实例分成两档判决。
/// 触发场景是休眠/关机/重启/手动改时钟后重新扫描（设计文档 6.2 唤醒补偿）。
/// 目的是「刚错过还来得及的就补发，早过头的合并成一条摘要，避免一口气弹一堆」。
/// 纯算法——now / since / 宽限期均由调用方传入，不读系统时钟。
/// </summary>
public static class MissedCompensator
{
    /// <summary>错过汇总摘要的文案模板：{0} = 过去多少小时，{1} = 错过条数。</summary>
    private const string SUMMARY_FORMAT = "过去 {0} 小时错过 {1} 条提醒";

    /// <summary>
    /// 对一轮扫描窗口内的未触发实例做补偿判决。
    /// 判定顺序（逐条）：
    /// <list type="number">
    /// <item>已触发（FiredAt 不为 null）或已处于终态（Done/Skipped/Missed）→ 不动。</item>
    /// <item>计划时刻早于 <paramref name="since"/>（本轮窗口之前，已在上轮处理）→ 不动。</item>
    /// <item>计划时刻晚于 <paramref name="now"/> → 未到点，不动。</item>
    /// <item>计划时刻落在 [now - 宽限, now] → <see cref="MissedVerdict.Resend"/>。</item>
    /// <item>更早 → <see cref="MissedVerdict.MarkMissed"/>，并计入合并摘要。</item>
    /// </list>
    /// </summary>
    /// <param name="pending">本轮涉及的实例（通常为 Pending/Snoozed 集合）。</param>
    /// <param name="now">当前时刻（本地墙钟）。</param>
    /// <param name="since">本轮扫描窗口起点（上次正常扫描时刻）。</param>
    /// <param name="graceMinutes">宽限期（分钟）。</param>
    public static CompensationResult Evaluate(
        IEnumerable<Occurrence> pending,
        DateTime now,
        DateTime since,
        int graceMinutes)
    {
        var toResend = new List<Occurrence>();
        var toMarkMissed = new List<Occurrence>();
        var graceStart = now.AddMinutes(-graceMinutes);

        foreach (var occurrence in pending)
        {
            if (occurrence.FiredAt is not null)
            {
                continue;
            }

            if (occurrence.Status is OccurrenceStatus.Done
                or OccurrenceStatus.Skipped
                or OccurrenceStatus.Missed)
            {
                continue;
            }

            if (occurrence.PlannedAt < since || occurrence.PlannedAt > now)
            {
                continue;
            }

            if (occurrence.PlannedAt >= graceStart)
            {
                toResend.Add(occurrence);
            }
            else
            {
                toMarkMissed.Add(occurrence);
            }
        }

        var summary = BuildSummary(toMarkMissed.Count, now, since);
        return new CompensationResult(toResend, toMarkMissed, summary);
    }

    /// <summary>构造错过汇总摘要文案；无错过时返回 null。</summary>
    private static string? BuildSummary(int missedCount, DateTime now, DateTime since)
    {
        if (missedCount == 0)
        {
            return null;
        }

        var hours = (int)Math.Round((now - since).TotalHours, MidpointRounding.AwayFromZero);
        if (hours < 1)
        {
            hours = 1; // 不足一小时按一小时表述，避免出现「过去 0 小时」。
        }

        return string.Format(SUMMARY_FORMAT, hours, missedCount);
    }
}
