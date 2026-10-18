namespace FuShi.Domain.Time;

/// <summary>
/// 时区与夏令时的归一化工具（设计文档 2.3 ③ 隐含要求，本工程把它显式化）。
/// 项目中时间一律以「本地墙钟 + IANA 时区」表示，跨英/中两地且要正确处理英国 DST 切换。
/// 本类只做两件事：把 IANA 时区 ID 解析成 <see cref="TimeZoneInfo"/>；把本地墙钟归一化到有效时刻。
/// </summary>
public static class TimeZoneResolver
{
    /// <summary>
    /// IANA 时区 ID 与 Windows 时区 ID 的兜底映射。
    /// .NET 6+ 在 Windows 上已支持 IANA ID，但为稳妥（个别环境 ICU 缺失）保留一份映射。
    /// </summary>
    private static readonly Dictionary<string, string> WINDOWS_FALLBACK_IDS =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TimeDefaults.DEFAULT_TIMEZONE_ID] = "GMT Standard Time",
            ["Asia/Shanghai"] = "China Standard Time",
        };

    /// <summary>
    /// 解析时区。优先按给定 ID（IANA）查找；失败时按兜底映射查 Windows ID；再失败退回 UTC。
    /// 返回结果带 <see cref="TimeZoneLookup.UsedFallback"/>，调用方据此记录日志，不静默降级。
    /// </summary>
    /// <param name="timezoneId">时区 ID（通常为 IANA，如 Europe/London）。</param>
    public static TimeZoneLookup Resolve(string timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return new TimeZoneLookup(TimeZoneInfo.Utc, UsedFallback: true);
        }

        var direct = TryFind(timezoneId);
        if (direct is not null)
        {
            return new TimeZoneLookup(direct, UsedFallback: false);
        }

        if (WINDOWS_FALLBACK_IDS.TryGetValue(timezoneId, out var windowsId))
        {
            var mapped = TryFind(windowsId);
            if (mapped is not null)
            {
                return new TimeZoneLookup(mapped, UsedFallback: true);
            }
        }

        return new TimeZoneLookup(TimeZoneInfo.Utc, UsedFallback: true);
    }

    /// <summary>
    /// 把「本地墙钟时刻」归一化到该时区下的有效时刻。
    /// <list type="bullet">
    /// <item>无效时刻（DST 开始跳过的 1 小时）→ 顺延到该日第一个有效时刻，记为 <see cref="DstAdjustmentKind.InvalidShiftedForward"/>。</item>
    /// <item>歧义时刻（DST 结束重复的 1 小时）→ 保持墙钟值不变，标记取「第一个」（夏令时侧）。</item>
    /// </list>
    /// 时刻一律以 Kind = Unspecified 参与判断，避免被系统按 UTC 解释。
    /// </summary>
    /// <param name="localWallClock">待归一化的本地墙钟时刻。</param>
    /// <param name="timeZone">目标时区。</param>
    public static TimeZoneResolution Normalize(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            // 逐分钟推进到第一个有效时刻。DST 跳变幅度固定且有限（英国为 1 小时），循环必然收敛。
            var shifted = local;
            while (timeZone.IsInvalidTime(shifted))
            {
                shifted = shifted.AddMinutes(1);
            }

            return new TimeZoneResolution(shifted, DstAdjustmentKind.InvalidShiftedForward);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // 墙钟值本身无需改动（歧义只在换算 UTC 时才需选边）；这里标记取夏令时侧。
            return new TimeZoneResolution(local, DstAdjustmentKind.AmbiguousPickedFirst);
        }

        return new TimeZoneResolution(local, DstAdjustmentKind.None);
    }

    /// <summary>按 ID 查找时区，找不到或无效时返回 null（由调用方决定兜底策略）。</summary>
    private static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
