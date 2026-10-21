namespace FuShi.Domain.Assistant;

/// <summary>
/// 19 种情绪的渲染预设表与动效常量，是设计稿数值的<b>唯一来源</b>。
/// <para>
/// 所有形变数值按设计稿的 120×120 坐标系书写，UI 层直接照用，不做换算；
/// 改动其中任何一个数字都应与设计稿同步核对。
/// </para>
/// </summary>
public static class BotExpressionCatalog
{
    // ---------- 动效常量（设计稿第 04 节） ----------

    /// <summary>稳态切换时长（毫秒）。设计稿为 260–420ms，取中值避免过冲像「抽一下」或高频切换显拖。</summary>
    public const int STEADY_TRANSITION_MS = 320;

    /// <summary>瞬态回落时长（毫秒）。回落要比切换利落，不占满 420ms。</summary>
    public const int TRANSIENT_RETURN_MS = 260;

    /// <summary>光色过渡时长（毫秒）。设计稿：光色随形状一起 180ms 过渡。</summary>
    public const int GLOW_TRANSITION_MS = 180;

    /// <summary>主动眨眼的间隔下限（毫秒）。设计稿：每 4–6 秒一次。</summary>
    public const int BLINK_INTERVAL_MIN_MS = 4000;

    /// <summary>主动眨眼的间隔上限（毫秒）。</summary>
    public const int BLINK_INTERVAL_MAX_MS = 6000;

    /// <summary>闭眼动画时长（毫秒）。</summary>
    public const int BLINK_CLOSE_MS = 70;

    /// <summary>睁眼动画时长（毫秒）。</summary>
    public const int BLINK_OPEN_MS = 100;

    /// <summary>张望的间隔下限（毫秒）。设计稿：每 25–40 秒一次。</summary>
    public const int WANDER_INTERVAL_MIN_MS = 25000;

    /// <summary>张望的间隔上限（毫秒）。</summary>
    public const int WANDER_INTERVAL_MAX_MS = 40000;

    /// <summary>张望偏出去的时间（毫秒）。</summary>
    public const int WANDER_OUT_MS = 240;

    /// <summary>张望收回来的时间（毫秒）。</summary>
    public const int WANDER_BACK_MS = 420;

    /// <summary>「刚下课」的判定窗口（分钟），用于 break。</summary>
    public const int BREAK_WINDOW_MINUTES = 10;

    /// <summary>多久无交互算「长期未交互」（设计稿：&gt; 2 小时）。</summary>
    public static readonly TimeSpan SLEEPY_AFTER = TimeSpan.FromHours(2);

    // ---------- 预设表 ----------

    /// <summary>按枚举顺序排列的全部预设（19 条）。</summary>
    private static readonly BotExpressionPreset[] PRESETS = BuildPresets();

    /// <summary>全部预设（只读）。</summary>
    public static IReadOnlyList<BotExpressionPreset> All => PRESETS;

    /// <summary>
    /// 取某种情绪的预设。
    /// </summary>
    /// <param name="expression">情绪。</param>
    /// <returns>渲染参数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">枚举值与预设表不同步时抛出，属于开发期错误。</exception>
    public static BotExpressionPreset Get(BotExpression expression)
    {
        foreach (var preset in PRESETS)
        {
            if (preset.Expression == expression)
            {
                return preset;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(expression), expression, "该情绪没有渲染预设，请同步 BotExpressionCatalog。");
    }

    /// <summary>是否为瞬态情绪（播完必须回落）。</summary>
    /// <param name="expression">情绪。</param>
    public static bool IsTransient(BotExpression expression) => Get(expression).IsTransient;

    /// <summary>
    /// 构造预设表。数值逐条对应设计稿 CSS 形变预设（第 197–223 行）。
    /// <para>
    /// blink / wander 的时长来自设计稿的瞬态回退规则；wink 与 dizzy 在情绪卡里标了「瞬态」但没给时长，
    /// 这里分别取 600ms / 900ms，与同组的眨眼、大笑节奏对齐。
    /// </para>
    /// </summary>
    private static BotExpressionPreset[] BuildPresets() => new[]
    {
        // ---------- 常驻 ----------
        Steady(BotExpression.Idle, EyeShape.Oval, EyeDeformation.Identity, GlowTone.Ice),
        Transient(BotExpression.Blink, EyeDeformation.Identity with { ScaleY = 0.08 }, 80),
        Transient(BotExpression.Wander, EyeDeformation.Identity with { TranslateX = -3.5, TranslateY = -1 }, 600),
        Steady(
            BotExpression.Sleepy,
            EyeShape.Oval,
            EyeDeformation.Identity with { ScaleY = 0.30, TranslateY = 4 },
            GlowTone.Ice),

        // ---------- 课程 ----------
        Steady(BotExpression.Focus, EyeShape.Oval, EyeDeformation.Identity with { ScaleY = 0.58 }, GlowTone.Ice),
        Steady(
            BotExpression.Soon,
            EyeShape.Oval,
            EyeDeformation.Identity with { ScaleX = 1.1, ScaleY = 1.1 },
            GlowTone.Amber),
        Steady(BotExpression.Done, EyeShape.Arc, EyeDeformation.Identity, GlowTone.Mint),
        Steady(BotExpression.Late, EyeShape.Exclamation, EyeDeformation.Identity, GlowTone.Coral),
        Steady(BotExpression.Break, EyeShape.Oval, EyeDeformation.Identity with { ScaleY = 0.10 }, GlowTone.Ice),

        // ---------- 社交 ----------
        Steady(BotExpression.Happy, EyeShape.Arc, EyeDeformation.Identity, GlowTone.Ice),
        Transient(BotExpression.Laugh, EyeShape.ArcDeep, EyeDeformation.Identity, 900),
        new BotExpressionPreset(
            BotExpression.Wink,
            EyeShape.Oval,
            EyeDeformation.Identity with { ScaleY = 0.08 },
            EyeDeformation.Identity,
            GlowTone.Ice,
            IsTransient: true,
            HoldMilliseconds: 600),
        Steady(BotExpression.Love, EyeShape.Heart, EyeDeformation.Identity, GlowTone.Rose),
        Steady(
            BotExpression.Smug,
            EyeShape.Oval,
            EyeDeformation.Identity with { RotateDegrees = -8, ScaleY = 0.52 },
            EyeDeformation.Identity with { RotateDegrees = 8, ScaleY = 0.52 },
            GlowTone.Ice),
        Steady(
            BotExpression.Shy,
            EyeShape.Oval,
            EyeDeformation.Identity with { TranslateX = -2.5, TranslateY = 2.5, ScaleY = 0.9 },
            GlowTone.Ice),

        // ---------- 情绪 ----------
        Steady(
            BotExpression.Sad,
            EyeShape.Oval,
            EyeDeformation.Identity with { TranslateY = 3.5, ScaleY = 0.6 },
            GlowTone.Ice),
        Steady(
            BotExpression.Angry,
            EyeShape.Oval,
            EyeDeformation.Identity with { RotateDegrees = 13, ScaleY = 0.6, TranslateY = 1 },
            EyeDeformation.Identity with { RotateDegrees = -13, ScaleY = 0.6, TranslateY = 1 },
            GlowTone.Ice),
        Transient(BotExpression.Surprise, EyeShape.Circle, EyeDeformation.Identity, 1200),
        Transient(BotExpression.Dizzy, EyeShape.Cross, EyeDeformation.Identity, 900),
    };

    /// <summary>构造对称的稳态预设（左右眼同形变）。</summary>
    private static BotExpressionPreset Steady(
        BotExpression expression,
        EyeShape shape,
        EyeDeformation eye,
        GlowTone glow) =>
        new(expression, shape, eye, eye, glow, IsTransient: false, HoldMilliseconds: 0);

    /// <summary>构造左右不对称的稳态预设。</summary>
    private static BotExpressionPreset Steady(
        BotExpression expression,
        EyeShape shape,
        EyeDeformation left,
        EyeDeformation right,
        GlowTone glow) =>
        new(expression, shape, left, right, glow, IsTransient: false, HoldMilliseconds: 0);

    /// <summary>构造对称的瞬态预设（光色固定冰白：瞬态不改光色）。</summary>
    private static BotExpressionPreset Transient(
        BotExpression expression,
        EyeDeformation eye,
        int holdMilliseconds) =>
        new(expression, EyeShape.Oval, eye, eye, GlowTone.Ice, IsTransient: true, holdMilliseconds);

    /// <summary>构造指定眼形的瞬态预设（laugh / surprise / dizzy 用，左右同形变）。</summary>
    private static BotExpressionPreset Transient(
        BotExpression expression,
        EyeShape shape,
        EyeDeformation eye,
        int holdMilliseconds) =>
        new(expression, shape, eye, eye, GlowTone.Ice, IsTransient: true, holdMilliseconds);
}
