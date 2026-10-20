namespace FuShi.Domain.Scheduling;

/// <summary>
/// 周网格投影的输入项：一条课表模板（已关联好科目名与颜色）。
/// 由 Infrastructure 层从「ScheduleEntry ⋈ Subject」组装后交给纯算法 <see cref="WeekGridProjector"/>，
/// 使投影逻辑与数据库、WPF 完全解耦、可独立单测。
/// </summary>
/// <param name="EntryId">来源课表条目主键（编辑时回写用）。</param>
/// <param name="DayOfWeek">模板按周重复的星期几。</param>
/// <param name="StartTime">开始时刻（本地墙钟）。</param>
/// <param name="EndTime">结束时刻；小于开始时刻表示跨午夜。</param>
/// <param name="SubjectName">科目名（展示用）。</param>
/// <param name="SubjectColor">科目颜色（#RRGGBB）。</param>
/// <param name="Note">备注（可选）。</param>
/// <param name="Enabled">是否启用；停用的模板在网格里以弱化样式呈现。</param>
/// <param name="ShiftMinutes">该条目<b>今天</b>被「稍后」当日顺延的累计分钟数（M3c）；0 = 未顺延。由 Infrastructure
/// 层查「今天」的实例填好，仅当 <paramref name="DayOfWeek"/> 恰为今天时才有非零值（顺延只影响今天这一列）。</param>
/// <param name="IsSkipped">该条目<b>今天</b>这一次是否被用户「跳过」（M3c 可见反馈）；由 Infrastructure 层查「今天」
/// 的实例填好，仅当 <paramref name="DayOfWeek"/> 恰为今天时才有意义（跳过只作用于今天这一次）。</param>
public sealed record WeekGridSource(
    int EntryId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string SubjectName,
    string SubjectColor,
    string? Note,
    bool Enabled,
    int ShiftMinutes = 0,
    bool IsSkipped = false);
