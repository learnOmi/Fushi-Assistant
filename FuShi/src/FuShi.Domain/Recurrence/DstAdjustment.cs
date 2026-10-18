using FuShi.Domain.Enums;
using FuShi.Domain.Time;

namespace FuShi.Domain.Recurrence;

/// <summary>
/// 一次 DST 调整记录：展开过程中某个局部时刻被时区归一化改动过。
/// 上层（引擎/启动流程）据此写日志——「绝不静默丢弃」的落地形式。
/// </summary>
/// <param name="SourceType">来源类型。</param>
/// <param name="SourceId">来源主键。</param>
/// <param name="Original">原始本地墙钟时刻。</param>
/// <param name="Adjusted">归一化后的本地墙钟时刻。</param>
/// <param name="Kind">调整类型。</param>
public sealed record DstAdjustment(
    OwnerType SourceType,
    int SourceId,
    DateTime Original,
    DateTime Adjusted,
    DstAdjustmentKind Kind);
