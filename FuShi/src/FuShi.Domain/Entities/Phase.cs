using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 阶段：课表的时间容器（设计文档 2.1）。一个阶段承载一批课表条目，
/// 例如「考研冲刺 2026 春」「实习期」。同一时间可有多个阶段，靠 IsActive 标记当前生效者。
/// </summary>
public class Phase
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>阶段名，如「考研冲刺」。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>生效起始日（含）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>生效结束日（含）；null 表示无限期。</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>是否为当前生效阶段。</summary>
    public bool IsActive { get; set; }

    /// <summary>该阶段下的课表条目（一对多）。</summary>
    public ICollection<ScheduleEntry> Entries { get; set; } = new List<ScheduleEntry>();
}
