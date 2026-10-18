namespace FuShi.Domain.Enums;

/// <summary>
/// 事项优先级（设计文档 2.1 Task.priority）。
/// 影响提醒力度与排序；不是「重要度打分」，只做三档粗分。
/// </summary>
public enum TaskPriority
{
    /// <summary>低。</summary>
    Low = 0,

    /// <summary>普通（默认）。</summary>
    Normal = 1,

    /// <summary>高。</summary>
    High = 2,
}
