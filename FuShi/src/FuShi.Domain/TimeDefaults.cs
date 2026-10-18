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
}
