using FuShi.Domain.Entities;
using FuShi.Domain.Recurrence;

namespace FuShi.Domain.Dedup;

/// <summary>
/// 去重算法：从「新展开的计划实例」中剔除「已存在」的，同时去掉批内自身的重复。
/// 判据是 <see cref="OccurrenceKey"/> 三元组；与数据库唯一索引双保险。
/// 纯算法——输入两个集合，输出需要新增的集合，不碰数据库。
/// </summary>
public static class OccurrenceDeduplicator
{
    /// <summary>
    /// 过滤出尚未存在的计划实例。
    /// </summary>
    /// <param name="candidates">新展开的计划实例。</param>
    /// <param name="existing">库中已有的 Occurrence。</param>
    /// <returns>需要新增的计划实例（保持入参顺序）。</returns>
    public static IReadOnlyList<PlannedOccurrence> RemoveExisting(
        IEnumerable<PlannedOccurrence> candidates,
        IEnumerable<Occurrence> existing)
    {
        var seen = new HashSet<OccurrenceKey>();

        foreach (var occurrence in existing)
        {
            seen.Add(new OccurrenceKey(occurrence.SourceType, occurrence.SourceId, occurrence.PlannedAt));
        }

        var result = new List<PlannedOccurrence>();
        foreach (var candidate in candidates)
        {
            // 用 HashSet.Add 的返回值：既能剔除「与库中重复」，也能顺带剔除「批内重复」。
            if (seen.Add(OccurrenceKey.From(candidate)))
            {
                result.Add(candidate);
            }
        }

        return result;
    }
}
