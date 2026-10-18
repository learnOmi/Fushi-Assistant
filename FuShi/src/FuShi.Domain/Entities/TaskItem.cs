using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 事项：一次性 / 自由重复的「杂项提醒」（设计文档 2.1 Task）。
/// 与课表条目的区别：课表按「周几」周期模板重复，事项按自身 DueAt/重复规则触发。
/// 类名用 <c>TaskItem</c> 以避免与 <see cref="System.Threading.Tasks.Task"/> 冲突，
/// 映射到数据库表名 <c>Tasks</c>（见 FuShiDbContext）。
/// </summary>
public class TaskItem
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>截止时刻（本地墙钟）；null 表示无明确截止。</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>
    /// 重复规则文本（RRULE 风格，M3 再解析）；null 表示一次性事项。
    /// 存储层先留字段，避免 M3 再改表。
    /// </summary>
    public string? RepeatRule { get; set; }

    /// <summary>优先级。</summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>状态。</summary>
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;
}
