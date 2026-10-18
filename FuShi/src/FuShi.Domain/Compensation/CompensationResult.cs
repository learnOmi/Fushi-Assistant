using FuShi.Domain.Entities;

namespace FuShi.Domain.Compensation;

/// <summary>
/// 错过补偿的判决输出：需要补发的实例、需要标记错过的实例，以及一条合并摘要。
/// </summary>
/// <param name="ToResend">需要立即补发的实例（宽限期内）。</param>
/// <param name="ToMarkMissed">需要标记为错过的实例（超出宽限期）。</param>
/// <param name="SummaryText">合并摘要文案；无错过时为 null。</param>
public sealed record CompensationResult(
    IReadOnlyList<Occurrence> ToResend,
    IReadOnlyList<Occurrence> ToMarkMissed,
    string? SummaryText);
