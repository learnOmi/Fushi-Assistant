namespace FuShi.Domain.Enums;

/// <summary>
/// 完成记录状态（设计文档 2.1 CompletionLog.status）。
/// 统计页的完成率分母只看「应该发生的时段」，Done 计分子，Skipped/Missed 只入分母。
/// </summary>
public enum CompletionStatus
{
    /// <summary>已完成。</summary>
    Done = 0,

    /// <summary>已跳过（用户主动放弃）。</summary>
    Skipped = 1,

    /// <summary>已错过（未响应且超出宽限期）。</summary>
    Missed = 2,
}
