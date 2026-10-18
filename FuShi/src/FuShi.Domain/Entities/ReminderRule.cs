using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 提醒规则：描述「何时、以什么方式提醒」（设计文档 2.1）。
/// 通过 (OwnerType, OwnerId) 多态地挂在课表条目或事项上——一个物主可挂多条规则。
/// </summary>
public class ReminderRule
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>物主类型（课表条目 / 事项）。</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>物主主键（对应 ScheduleEntry.Id 或 TaskItem.Id）。</summary>
    public int OwnerId { get; set; }

    /// <summary>触发时机（准点 / 提前 / 结束）。</summary>
    public TriggerType TriggerType { get; set; }

    /// <summary>
    /// 相对偏移分钟数。配合 TriggerType：BeforeStart 表示「提前多少分钟」。
    /// AtStart 时忽略。
    /// </summary>
    public int OffsetMinutes { get; set; }

    /// <summary>提醒通道（可多选，按位组合）。</summary>
    public ReminderChannel Channels { get; set; } = ReminderChannel.Popup;

    /// <summary>提醒力度。</summary>
    public ReminderStrength Strength { get; set; } = ReminderStrength.Normal;
}
