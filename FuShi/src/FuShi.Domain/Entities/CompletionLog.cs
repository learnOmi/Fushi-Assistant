using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 完成记录：统计的原料（设计文档 2.1）。
/// 每次时段结束时落一条，由统计服务按日期聚合出完成率与科目分配。
/// </summary>
public class CompletionLog
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>所属日期（用于按天聚合）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>对应的课表条目主键。</summary>
    public int EntryId { get; set; }

    /// <summary>计划时长（分钟）。</summary>
    public int PlannedMin { get; set; }

    /// <summary>实际时长（分钟）。</summary>
    public int ActualMin { get; set; }

    /// <summary>完成状态（完成 / 跳过 / 错过）。</summary>
    public CompletionStatus Status { get; set; }
}
