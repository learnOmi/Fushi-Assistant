namespace FuShi.Domain.Time;

/// <summary>
/// 一次时区归一化的结果。
/// </summary>
/// <param name="LocalTime">归一化后的本地墙钟时刻（Kind = Unspecified）。</param>
/// <param name="Kind">本次发生的调整类型，供上层记录日志。</param>
public readonly record struct TimeZoneResolution(DateTime LocalTime, DstAdjustmentKind Kind);

/// <summary>
/// 时区解析结果。
/// </summary>
/// <param name="Zone">解析到的时区。</param>
/// <param name="UsedFallback">是否用了兜底（IANA 未识别、退回 Windows ID 或 UTC），调用方应记日志。</param>
public readonly record struct TimeZoneLookup(TimeZoneInfo Zone, bool UsedFallback);
