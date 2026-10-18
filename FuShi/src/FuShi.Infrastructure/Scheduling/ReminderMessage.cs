using FuShi.Domain.Enums;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 一条待派发的提醒内容（引擎产出、通知通道消费）。
/// 引擎只负责「把该提醒的内容组装好」，具体走哪个通道、要不要降级由通道实现决定。
/// </summary>
/// <param name="Title">标题（通常是科目名或「错过提醒汇总」）。</param>
/// <param name="Body">正文（如「14:00 开始」或错过摘要）。</param>
/// <param name="Strength">提醒力度（影响通道的打断强度）。</param>
public sealed record ReminderMessage(string Title, string Body, ReminderStrength Strength);
