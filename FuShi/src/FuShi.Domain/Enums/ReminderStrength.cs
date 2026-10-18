namespace FuShi.Domain.Enums;

/// <summary>
/// 提醒力度（设计文档 5.9 提醒分档）。
/// 与免打扰时段配合：Quiet Hours 内一律降级，力度决定「打断强度」的上限。
/// </summary>
public enum ReminderStrength
{
    /// <summary>轻提醒：只动图标的眼神/颜色，不弹窗不发声。</summary>
    Soft = 0,

    /// <summary>常规提醒：Toast + 自绘弹窗，不发声。</summary>
    Normal = 1,

    /// <summary>强提醒：弹窗 + 发声，用于关键节点（如考试当天）。</summary>
    Strong = 2,
}
