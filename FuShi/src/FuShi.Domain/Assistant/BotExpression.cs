namespace FuShi.Domain.Assistant;

/// <summary>
/// 黑团子的 19 种情绪（设计稿「浮时-助手定稿-黑团子」）。
/// <para>
/// 顺序与设计稿的四组一致（常驻 / 课程 / 社交 / 情绪），便于人工对照。
/// 情绪只描述「两只眼怎么变」，不携带任何业务含义；业务到情绪的映射见
/// <see cref="BotExpressionDirector"/>。
/// </para>
/// </summary>
public enum BotExpression
{
    // ---------- 常驻 ----------

    /// <summary>待机：两块屏安静地亮着。</summary>
    Idle,

    /// <summary>眨眼：自发瞬态，闭 80ms。</summary>
    Blink,

    /// <summary>张望：自发瞬态，缓慢看一眼别处再回来。</summary>
    Wander,

    /// <summary>困倦：眼睑垂到一半，长时间无交互时使用。</summary>
    Sleepy,

    // ---------- 课程 ----------

    /// <summary>专注：上睑压低，屏变窄（课程进行中）。</summary>
    Focus,

    /// <summary>临期：双眼放大 + 琥珀光（进入提前量窗口）。</summary>
    Soon,

    /// <summary>完成：上弧眼 + 薄荷绿光。</summary>
    Done,

    /// <summary>超时：屏上打出「!」，珊瑚红光。</summary>
    Late,

    /// <summary>小憩：完全闭合，课间使用。</summary>
    Break,

    // ---------- 社交 ----------

    /// <summary>开心：两块屏弯成上弧。</summary>
    Happy,

    /// <summary>大笑：弧更弯更挤，瞬态。</summary>
    Laugh,

    /// <summary>俏皮：眨单眼，瞬态。</summary>
    Wink,

    /// <summary>喜欢：屏变心形，唯一使用玫瑰光的情绪。</summary>
    Love,

    /// <summary>得意：双眼内扬的窄缝。</summary>
    Smug,

    /// <summary>害羞：眼神躲向斜下。</summary>
    Shy,

    // ---------- 情绪 ----------

    /// <summary>沮丧：眼睑下垂，看向下方。</summary>
    Sad,

    /// <summary>生气：双眼内倾的斜缝。</summary>
    Angry,

    /// <summary>惊讶：瞬间圆睁，瞬态。</summary>
    Surprise,

    /// <summary>晕眩：屏上打出两枚 X，瞬态。</summary>
    Dizzy,
}

/// <summary>
/// 脸上出现的形状层。除 <see cref="Oval"/> 外，其余形状出现时会隐藏两只竖椭圆眼。
/// </summary>
public enum EyeShape
{
    /// <summary>竖椭圆（默认眼形）。</summary>
    Oval,

    /// <summary>上弧（happy / done）。</summary>
    Arc,

    /// <summary>更弯的上弧（laugh）。</summary>
    ArcDeep,

    /// <summary>圆睁（surprise）。</summary>
    Circle,

    /// <summary>两枚 X（dizzy）。</summary>
    Cross,

    /// <summary>感叹号（late）。</summary>
    Exclamation,

    /// <summary>心形（love）。</summary>
    Heart,
}

/// <summary>
/// 眼睛的光色。<b>光色只表达状态</b>，唯一定稿破例是「喜欢」的玫瑰色。
/// </summary>
public enum GlowTone
{
    /// <summary>冰白 #CFE9FF：日常。</summary>
    Ice,

    /// <summary>琥珀 #FFC46A：临期。</summary>
    Amber,

    /// <summary>薄荷绿 #7DEBAA：完成。</summary>
    Mint,

    /// <summary>珊瑚红 #FF6B57：超时。</summary>
    Coral,

    /// <summary>玫瑰 #FF9DB0：喜欢（唯一例外）。</summary>
    Rose,
}
