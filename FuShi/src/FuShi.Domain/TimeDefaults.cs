namespace FuShi.Domain;

/// <summary>
/// 与「时间模型」相关的领域默认值（设计文档 2.3 ③ / 3.4）。
/// 集中常量，避免散布魔法值：时区、宽限期、展开视野。
/// </summary>
public static class TimeDefaults
{
    /// <summary>默认时区（IANA），用户往返中英，基线取英国。</summary>
    public const string DEFAULT_TIMEZONE_ID = "Europe/London";

    /// <summary>
    /// 错过补偿宽限期（分钟）。计划时刻落在 [now - 宽限, now] 内视为「刚错过、还来得及」，
    /// 立即补发；早于该窗口的合并成摘要。对应 ReminderRule/AppSetting.LeadTimeMinutes 的兜底值。
    /// </summary>
    public const int DEFAULT_GRACE_MINUTES = 30;

    /// <summary>
    /// 启动时向前展开的天数（设计文档 3.4：载入未来 7 天 Occurrence 装入最小堆）。
    /// </summary>
    public const int EXPANSION_HORIZON_DAYS = 7;

    /// <summary>「稍后」的默认延后分钟数（设计文档 5.4：稍后 10 分钟）。</summary>
    public const int DEFAULT_SNOOZE_MINUTES = 10;

    /// <summary>
    /// 顺延取数窗口的最大回看宽度（分钟，一天）。
    /// M3c「稍后」改为当日顺延后，实例的<b>有效触发点</b> = <c>PlannedAt + ShiftMinutes − 提前量</c>，
    /// 会比 <c>PlannedAt</c> 晚。为兼顾索引与正确性：SQL 区间只把下界按本常量放宽，再在内存里按有效时刻精筛。
    /// 取一天是<b>安全上界</b>——顺延量受「该课原结束时刻」与「当天 23:59」双重封顶，恒小于 24 小时。
    /// </summary>
    public const int SHIFT_WINDOW_SLACK_MINUTES = 24 * 60;
}
