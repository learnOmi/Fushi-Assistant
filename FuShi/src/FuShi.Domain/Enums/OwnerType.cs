namespace FuShi.Domain.Enums;

/// <summary>
/// 归属类型：一条提醒规则（ReminderRule）或一条提醒实例（Occurrence）到底挂在谁身上。
/// 课表条目本身只是「周期模板」，真正会触发的是由它展开出的 Occurrence，
/// 因此 Occurrence.SourceType 复用本枚举，指向展开它的源头（ScheduleEntry 或 Task）。
/// </summary>
public enum OwnerType
{
    /// <summary>归属课表条目（ScheduleEntry）。</summary>
    ScheduleEntry = 0,

    /// <summary>归属事项（Task）。</summary>
    Task = 1,
}
