namespace FuShi.Domain.Enums;

/// <summary>
/// 事项状态（设计文档 2.1 Task.status）。
/// 与课表条目不同，事项是一次性/自由重复的「杂项提醒」。
/// 命名加 <c>Item</c> 后缀，避免与 <see cref="System.Threading.Tasks.TaskStatus"/> 同名歧义。
/// </summary>
public enum TaskItemStatus
{
    /// <summary>待办。</summary>
    Pending = 0,

    /// <summary>已完成。</summary>
    Done = 1,

    /// <summary>已放弃。</summary>
    Skipped = 2,
}
