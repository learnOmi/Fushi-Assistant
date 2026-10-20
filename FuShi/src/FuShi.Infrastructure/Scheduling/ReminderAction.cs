namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 用户对一条提醒的动作（弹窗三按钮 + 未响应），M3b 新增。
/// 作为弹窗回调与 <see cref="ReminderResponseService"/> 之间的契约，避免在 UI 侧散落字符串。
/// </summary>
public enum ReminderAction
{
    /// <summary>开始：进入「进行中」，到结束时刻自动记 Done（可手点完成）。</summary>
    Start = 0,

    /// <summary>稍后：复用本实例回退 Pending（清 FiredAt）并链式叠 <c>ShiftMinutes</c>（当日顺延调课），到新触发点重弹。</summary>
    Snooze = 1,

    /// <summary>跳过：置 Skipped 并记一条 Skipped 完成记录。</summary>
    Skip = 2,

    /// <summary>完成：立即收成 Done 并记一条 Done 完成记录（进行中点「完成」用）。</summary>
    Finish = 3,

    /// <summary>未响应：30s 无操作或直接关闭，实例仍 Fired，仅记一条 Missed 完成记录。</summary>
    NoResponse = 4,
}
