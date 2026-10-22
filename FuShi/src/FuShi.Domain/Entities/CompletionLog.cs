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

    /// <summary>
    /// 落账时的科目快照（M4b，可空）。
    /// 记录「当时属于哪个科目」而不是靠 <see cref="EntryId"/> 回查：
    /// 条目被删除后历史仍可按科目统计，改条目科目也不会把过去的数据追溯改写。
    /// 为空表示未分类（旧数据回填失败或来源本身无科目）。
    /// </summary>
    public int? SubjectId { get; set; }

    /// <summary>计划时长（分钟）。</summary>
    public int PlannedMin { get; set; }

    /// <summary>实际时长（分钟）。</summary>
    public int ActualMin { get; set; }

    /// <summary>完成状态（完成 / 跳过 / 错过）。</summary>
    public CompletionStatus Status { get; set; }
}
