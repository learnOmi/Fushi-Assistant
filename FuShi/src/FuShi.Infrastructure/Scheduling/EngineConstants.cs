namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 提醒引擎的时序与文案常量。集中放置，避免引擎循环与触发逻辑里出现魔法值。
/// </summary>
internal static class EngineConstants
{
    /// <summary>
    /// 启动首轮扫描的回溯小时数：进程不在时错过的提醒靠这段窗口被补偿扫描捞回。
    /// 超出宽限期的会被标记为「错过」并合并成一条摘要，不会逐条轰炸。
    /// </summary>
    public const int STARTUP_LOOKBACK_HOURS = 24;

    /// <summary>
    /// 单次休眠的上限（分钟）。既作为「无待触发项」时的兜底休眠，也保证进程长期驻留时
    /// 会周期性重新展开课表视野（否则跨过 7 天视野后不再产生新实例）。
    /// </summary>
    public const int MAX_SLEEP_MINUTES = 60;

    /// <summary>单次休眠的下限（毫秒）。避免「下一触发点恰好是此刻」导致空转。</summary>
    public const int MIN_SLEEP_MILLISECONDS = 200;

    /// <summary>循环内发生异常后的重试间隔（毫秒）。</summary>
    public const int ERROR_RETRY_DELAY_MILLISECONDS = 5000;

    /// <summary>通知正文格式：{0} = 计划时刻（HH:mm）。</summary>
    public const string REMINDER_BODY_FORMAT = "{0} 开始";

    /// <summary>引擎内部时间显示格式。</summary>
    public const string CLOCK_FORMAT = "HH:mm";

    /// <summary>错过汇总通知的标题。</summary>
    public const string SUMMARY_TITLE = "错过提醒汇总";
}
