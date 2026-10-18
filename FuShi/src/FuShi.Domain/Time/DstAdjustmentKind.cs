namespace FuShi.Domain.Time;

/// <summary>
/// 本地墙钟时刻被时区归一化时发生的调整类型。
/// 用于把「DST 跳变」这件事显式传出去，交由上层记日志（绝不静默丢弃）。
/// </summary>
public enum DstAdjustmentKind
{
    /// <summary>无需调整（绝大多数情况）。</summary>
    None = 0,

    /// <summary>
    /// 无效时刻：本地钟在夏令时开始日被跳过（英国 3 月 01:00→02:00）。
    /// 已顺延到该日第一个有效时刻。
    /// </summary>
    InvalidShiftedForward = 1,

    /// <summary>
    /// 歧义时刻：本地钟在夏令时结束日重复（英国 10 月 02:00→01:00）。
    /// 已选定「第一个」（夏令时侧）解释。
    /// </summary>
    AmbiguousPickedFirst = 2,
}
