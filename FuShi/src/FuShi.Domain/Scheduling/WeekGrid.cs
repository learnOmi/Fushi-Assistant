namespace FuShi.Domain.Scheduling;

/// <summary>
/// 周网格里的一条课表项（模板，而非具体日期的实例）。
/// 与 <see cref="TimelineSlot"/> 的区别：后者是「已展开到具体日期」的实例，前者保留「周几」的模板语义，
/// 供课表编辑界面呈现"一周节奏"。
/// </summary>
/// <param name="EntryId">来源条目主键。</param>
/// <param name="StartTime">开始时刻。</param>
/// <param name="EndTime">结束时刻。</param>
/// <param name="SubjectName">科目名。</param>
/// <param name="SubjectColor">科目颜色（#RRGGBB）。</param>
/// <param name="Note">备注（可选）。</param>
/// <param name="IsCrossMidnight">是否跨午夜（结束时刻意归到起始日那一格）。</param>
/// <param name="IsCurrent">是否与当前时刻重合（高亮用，口径与悬浮组件一致）。</param>
/// <param name="IsEnabled">是否启用；停用项弱化显示。</param>
public sealed record WeekGridItem(
    int EntryId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string SubjectName,
    string SubjectColor,
    string? Note,
    bool IsCrossMidnight,
    bool IsCurrent,
    bool IsEnabled);

/// <summary>
/// 网格单元格：某个星期几 + 某个起始时刻下的条目集合。
/// 正常每格 0 或 1 条；同天同刻存在多条时不丢数据，由界面自行折叠展示。
/// </summary>
/// <param name="DayOfWeek">所属星期几。</param>
/// <param name="StartTime">该行对应的起始时刻。</param>
/// <param name="Items">该格内的条目（可能为空）。</param>
public sealed record WeekGridCell(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    IReadOnlyList<WeekGridItem> Items)
{
    /// <summary>该格是否为空（无任何条目）——空格的点击 = 以该天该时刻预填新建。</summary>
    public bool IsEmpty => Items.Count == 0;
}

/// <summary>
/// 网格行：一个起始时刻横跨七天。
/// <see cref="Cells"/> 恒为 7 项，索引 0..6 依次对应「周一…周日」（周一打头，符合使用习惯）。
/// </summary>
/// <param name="StartTime">本行对应的起始时刻（升序排列）。</param>
/// <param name="Cells">七天的单元格。</param>
public sealed record WeekGridRow(TimeOnly StartTime, IReadOnlyList<WeekGridCell> Cells);

/// <summary>
/// 周网格：课表模板的「7 列 × N 行」可视化结果。
/// 行 = 当前阶段内出现过的<b>去重起始时刻</b>升序；列 = 周一…周日。
/// </summary>
/// <param name="Rows">行集合（升序）。</param>
public sealed record WeekGrid(IReadOnlyList<WeekGridRow> Rows)
{
    /// <summary>空网格（无任何模板时的兜底）。</summary>
    public static WeekGrid Empty { get; } = new(Array.Empty<WeekGridRow>());

    /// <summary>是否为空课表。</summary>
    public bool IsEmpty => Rows.Count == 0;
}
