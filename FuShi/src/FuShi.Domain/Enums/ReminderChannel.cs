namespace FuShi.Domain.Enums;

/// <summary>
/// 提醒通道（设计文档 2.1 ReminderRule.channels，可多选）。
/// 按位组合：一条规则可以同时走系统 Toast + 自绘弹窗（ADR-4 双通道）。
/// </summary>
[Flags]
public enum ReminderChannel
{
    /// <summary>无通道（仅落库、不发声）。</summary>
    None = 0,

    /// <summary>系统 Toast（免打扰时唯一的保底通道）。</summary>
    Toast = 1 << 0,

    /// <summary>自绘弹窗（有表现力的主通道）。</summary>
    Popup = 1 << 1,

    /// <summary>提示音。</summary>
    Sound = 1 << 2,
}
