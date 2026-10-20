namespace FuShi.Domain.Scheduling;

/// <summary>
/// 链式顺延的一条输入项：某天某条实例的计划时刻、原结束时刻与已累计的顺延量。
/// 由 Infrastructure 层从「当天 Occurrence ⋈ ScheduleEntry」组装后交给纯算法 <see cref="ShiftChain"/>，
/// 使顺延逻辑与数据库、UI 完全解耦、可独立单测。
/// </summary>
/// <param name="OccurrenceId">实例主键（回写用）。</param>
/// <param name="PlannedAt">模板计划开始时刻（本地墙钟，Kind = Unspecified）。恒保持模板时刻不变。</param>
/// <param name="EndsAt">模板原结束时刻；跨午夜条目为次日（未叠顺延）。</param>
/// <param name="ShiftMinutes">已累计的顺延分钟数（多数为 0）。</param>
public sealed record ShiftChainItem(
    int OccurrenceId,
    DateTime PlannedAt,
    DateTime EndsAt,
    int ShiftMinutes);

/// <summary>链式顺延的一条结果：该条实例应写入的新顺延量（已在旧值基础上叠加）。</summary>
/// <param name="OccurrenceId">实例主键。</param>
/// <param name="NewShiftMinutes">应写入的新顺延量。</param>
public sealed record ShiftChainChange(int OccurrenceId, int NewShiftMinutes);

/// <summary>
/// 链式顺延结果。
/// </summary>
/// <param name="Changes">发生变更的实例集合（按计划时刻升序）；空集合 = 无一条顺延。</param>
/// <param name="CeilingReached">是否触顶 —— 某条顺延后的落点越过封顶线，链条就此终止；触顶时调用方记一条日志。</param>
public sealed record ShiftChainResult(IReadOnlyList<ShiftChainChange> Changes, bool CeilingReached)
{
    /// <summary>无变更、未触顶的空结果。</summary>
    public static ShiftChainResult Empty { get; } = new(Array.Empty<ShiftChainChange>(), false);
}

/// <summary>
/// 链式顺延算法（M3c 核心，纯函数）：把用户点了「稍后」的这条课往后挪，并连带推挤真被它压到的后续课。
/// <para>
/// 口径（见 <c>M3-实施计划.md</c> 第 11.1 / 11.4 / 11.7 节）：
/// <list type="bullet">
/// <item><b>只顺延今天这一次</b>：算法只作用于「同一批实例」（同一天），模板不动。</item>
/// <item><b>整块同长顺移</b>：只增 <c>ShiftMinutes</c>，开始与结束同时后移，总时长不变。</item>
/// <item><b>链式顺延</b>：从被点那条起往后扫；下一条的<b>有效开始</b>若小于上一条的<b>有效结束</b>（确实重叠）
/// 就也 <c>+= shiftMinutes</c> 并继续；一旦不再重叠，链条立即终止，后面的课一律不动。</item>
/// <item><b>封顶</b>：任一条顺延后的有效开始若越过封顶线，则该条<b>不再顺延、链条立即终止</b>。封顶线有两条：
/// ① 越过当天 23:59（硬截断，绝不把课推过第二天）；② 越过该条<b>自身课程的原结束时刻</b>
/// （不给已下课的时段发陈旧提醒）。</item>
/// </list>
/// </para>
/// <para>
/// 算法不读系统时钟、不碰数据库：排序、重叠判定、封顶全部基于传入的数据，故可 100% 单测。
/// </para>
/// </summary>
public static class ShiftChain
{
    /// <summary>
    /// 计算一次「稍后」触发的顺延变更。
    /// </summary>
    /// <param name="items">当天参与链式判定的一批实例（含被点那条与它后面的课）；调用方保证同属一天。</param>
    /// <param name="targetOccurrenceId">被点「稍后」的实例主键。</param>
    /// <param name="shiftMinutes">本次顺延量（分钟），即「稍后 N 分钟」的 N。</param>
    /// <returns>变更集合与是否触顶；<paramref name="targetOccurrenceId"/> 不在集合内或顺延量非正时返回空结果。</returns>
    public static ShiftChainResult Apply(
        IEnumerable<ShiftChainItem> items,
        int targetOccurrenceId,
        int shiftMinutes)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (shiftMinutes <= 0)
        {
            return ShiftChainResult.Empty;
        }

        var ordered = items
            .OrderBy(static item => item.PlannedAt)
            .ThenBy(static item => item.OccurrenceId)
            .ToList();

        var targetIndex = ordered.FindIndex(item => item.OccurrenceId == targetOccurrenceId);
        if (targetIndex < 0)
        {
            return ShiftChainResult.Empty;
        }

        // 封顶线①：当天下一天的零点（越过 23:59 即触顶）。
        var midnight = DateOnly.FromDateTime(ordered[targetIndex].PlannedAt)
            .AddDays(1)
            .ToDateTime(TimeOnly.MinValue);

        var changes = new List<ShiftChainChange>();
        DateTime? previousEffectiveEnd = null;

        for (var index = targetIndex; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var effectiveStart = item.PlannedAt.AddMinutes(item.ShiftMinutes);

            // 被点那条必推；其后的课只在「真被压到」时才推，否则链条立即终止。
            var needsShift = index == targetIndex
                || (previousEffectiveEnd is not null && effectiveStart < previousEffectiveEnd.Value);

            if (!needsShift)
            {
                break;
            }

            var newShift = item.ShiftMinutes + shiftMinutes;
            var shiftedStart = item.PlannedAt.AddMinutes(newShift);

            // 封顶线②：落点越过该条课程自身的原结束时刻 → 会给已下课的时段发陈旧提醒，不再顺延。
            if (shiftedStart >= midnight || shiftedStart > item.EndsAt)
            {
                return new ShiftChainResult(changes, true);
            }

            changes.Add(new ShiftChainChange(item.OccurrenceId, newShift));
            previousEffectiveEnd = item.EndsAt.AddMinutes(newShift);
        }

        return new ShiftChainResult(changes, false);
    }
}
