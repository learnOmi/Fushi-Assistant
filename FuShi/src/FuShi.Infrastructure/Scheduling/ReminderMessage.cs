using FuShi.Domain.Enums;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 一条待派发的提醒内容（引擎产出、通知通道消费）。
/// 引擎只负责「把该提醒的内容组装好」，具体走哪个通道、要不要降级由通道实现决定。
/// </summary>
/// <param name="Title">标题（通常是科目名或「错过提醒汇总」）。</param>
/// <param name="Body">正文（如「14:00 开始」或错过摘要）。</param>
/// <param name="Strength">提醒力度（影响通道的打断强度）。</param>
/// <param name="OccurrenceId">
/// 对应的账本行主键（M3b）。弹窗的「开始/稍后/跳过」需要它才能回写事件；
/// 汇总类通知无明确归属，留 <c>null</c>（此时弹窗按「哑壳」呈现、无按钮）。
/// </param>
/// <param name="PlannedAt">课程开始时刻（展示用）。</param>
/// <param name="EndsAt">课程结束时刻（弹窗展示与「进行中」自动完成用）。</param>
/// <param name="IsTask">
/// 是否为事项提醒（M5b）。事项的主按钮语义是「完成」，课表是「开始」；
/// 弹窗据此决定按钮文案与映射的动作，避免 UI 再猜一次来源类型。
/// </param>
public sealed record ReminderMessage(
    string Title,
    string Body,
    ReminderStrength Strength,
    int? OccurrenceId = null,
    DateTime? PlannedAt = null,
    DateTime? EndsAt = null,
    bool IsTask = false);
