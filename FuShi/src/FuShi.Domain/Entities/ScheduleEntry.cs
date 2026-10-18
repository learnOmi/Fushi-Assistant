namespace FuShi.Domain.Entities;

/// <summary>
/// 课表条目：周期性「模板」，不是具体某天的实例（设计文档 2.3 ①）。
/// 只记「周几 + 起止时刻」，触发时由 <c>RecurrenceExpander</c> 按日期展开成 Occurrence。
/// 好处：改一次模板，全局立即生效；历史 Occurrence 不受影响。
/// </summary>
public class ScheduleEntry
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>所属阶段。</summary>
    public int PhaseId { get; set; }

    /// <summary>所属阶段的导航属性。</summary>
    public Phase? Phase { get; set; }

    /// <summary>一周中的第几天（模板按周重复）。</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>开始时刻（本地墙钟）。</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// 结束时刻（本地墙钟）。
    /// 若 EndTime &lt; StartTime，视为「跨午夜的一个连续时段」，不拆成两天。
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>科目。</summary>
    public int SubjectId { get; set; }

    /// <summary>科目导航属性。</summary>
    public Subject? Subject { get; set; }

    /// <summary>备注（可选）。</summary>
    public string? Note { get; set; }

    /// <summary>是否启用；停用的模板在展开时直接跳过。</summary>
    public bool Enabled { get; set; } = true;
}
