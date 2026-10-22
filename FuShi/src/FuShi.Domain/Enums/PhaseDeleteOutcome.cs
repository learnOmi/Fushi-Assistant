namespace FuShi.Domain.Enums;

/// <summary>
/// 删除阶段的业务结果（M4a）。
/// 阶段不能像普通配置那样"一删了之"：它挂着课表条目与历史完成记录，
/// 故把「为什么没删成」显式化，让界面能给出具体提示，而不是笼统的失败。
/// </summary>
public enum PhaseDeleteOutcome
{
    /// <summary>已删除（级联删除了该阶段下的课表条目）。</summary>
    Deleted = 0,

    /// <summary>阶段不存在（可能已被删除）。</summary>
    NotFound = 1,

    /// <summary>拒绝删除：该阶段正处于生效状态，需先切换到其他阶段。</summary>
    Active = 2,

    /// <summary>拒绝删除：该阶段的条目已产生完成记录，硬删会让历史统计失去归属。</summary>
    HasHistory = 3,
}
