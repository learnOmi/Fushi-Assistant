namespace FuShi.Domain.Enums;

/// <summary>
/// 触发时机（设计文档 2.1 ReminderRule.triggerType）。
/// 结合 offsetMinutes 决定「相对计划时刻往前/往后推多久」触发。
/// </summary>
public enum TriggerType
{
    /// <summary>准点触发：计划时刻到点即提醒（offsetMinutes 忽略）。</summary>
    AtStart = 0,

    /// <summary>提前提醒：比计划时刻早 offsetMinutes 触发（如课前 10 分钟）。</summary>
    BeforeStart = 1,

    /// <summary>结束提醒：在计划结束时刻触发（如自习结束收尾）。</summary>
    AtEnd = 2,
}
