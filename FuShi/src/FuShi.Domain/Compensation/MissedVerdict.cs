namespace FuShi.Domain.Compensation;

/// <summary>
/// 错过补偿的判决结果。
/// </summary>
public enum MissedVerdict
{
    /// <summary>立即补发：计划时刻落在宽限期内，还来得及提醒。</summary>
    Resend = 0,

    /// <summary>标记错过：已超出宽限期，只汇总、不逐条弹窗。</summary>
    MarkMissed = 1,
}
