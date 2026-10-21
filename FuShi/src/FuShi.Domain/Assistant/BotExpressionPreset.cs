namespace FuShi.Domain.Assistant;

/// <summary>
/// 单只眼的形变参数，数值直接对应设计稿 CSS 的 transform。
/// <para>
/// 合成顺序（落到 UI 时）必须与 CSS 文本顺序一致：Rotate → Scale → Translate，
/// 即 CSS 里 <c>rotate(-8deg) scaleY(.52)</c> 先缩放后旋转，<c>translate(...) scaleY(.9)</c> 先缩放后平移。
/// </para>
/// </summary>
/// <param name="ScaleX">水平缩放倍数，1 = 原样。</param>
/// <param name="ScaleY">垂直缩放倍数，1 = 原样；0.08 约等于闭眼。</param>
/// <param name="TranslateX">水平位移（设计稿坐标系 120×120 下的像素）。</param>
/// <param name="TranslateY">垂直位移（同上）。</param>
/// <param name="RotateDegrees">旋转角度（度），正值顺时针。</param>
public readonly record struct EyeDeformation(
    double ScaleX = 1,
    double ScaleY = 1,
    double TranslateX = 0,
    double TranslateY = 0,
    double RotateDegrees = 0)
{
    /// <summary>
    /// 不变形（原样）。
    /// <para>
    /// 不能用 <c>new()</c>：结构体的隐式无参构造会把字段清零，<c>ScaleX/ScaleY</c> 变成 0（眼睛直接消失），
    /// 不会走上面那些带默认值的参数。
    /// </para>
    /// </summary>
    public static EyeDeformation Identity => new(ScaleX: 1, ScaleY: 1);
}

/// <summary>
/// 一种情绪的全部渲染参数：眼形、左右眼形变、光色，以及它是否为瞬态。
/// <para>
/// 左右眼分开描述，是为了 <c>wink</c>（单眼眨）、<c>smug</c>（双眼内扬）、<c>angry</c>（内倾）这类不对称情绪；
/// 对称情绪左右传同一个值即可。
/// </para>
/// </summary>
/// <param name="Expression">对应的情绪。</param>
/// <param name="Shape">当前显示的眼形层。</param>
/// <param name="Left">左眼形变。</param>
/// <param name="Right">右眼形变。</param>
/// <param name="Glow">光色。</param>
/// <param name="IsTransient">true = 插播型瞬态，播完必须回落；false = 稳态，持续到信号变化。</param>
/// <param name="HoldMilliseconds">
/// 瞬态的保持时长（毫秒）；稳态为 0。
/// 对 blink 而言这是「完全闭眼」的驻留时间，不含闭合与睁开的过渡。
/// </param>
public sealed record BotExpressionPreset(
    BotExpression Expression,
    EyeShape Shape,
    EyeDeformation Left,
    EyeDeformation Right,
    GlowTone Glow,
    bool IsTransient,
    int HoldMilliseconds);
