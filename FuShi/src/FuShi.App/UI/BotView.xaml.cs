using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FuShi.Domain.Assistant;

namespace FuShi.UI;

/// <summary>
/// 团 · 黑团子：亮面鹅卵石黑壳 + 两只发光眼。
/// 无嘴无眉无腮红；全部情绪由两只眼的形状完成。
/// <para>
/// 本控件只负责「把预设画出来」：<see cref="Expression"/> 是稳态，由状态机算好推入；
/// <see cref="TransientExpression"/> 配合 <see cref="TransientSequence"/> 是插播请求。
/// 常驻眨眼/张望也走同一条瞬态通道，保证同一时刻只有一套动画在改眼部变换。
/// </para>
/// <para>
/// 动效原则：静止为主，偶发插播；瞬态必回退，回退目标是<b>当时的</b>稳态；
/// 禁止常驻呼吸、变色循环、摇晃。系统关闭动画效果时全部瞬切。
/// </para>
/// </summary>
public partial class BotView : UserControl
{
    /// <summary>稳态情绪（由课程状态机驱动，持续到信号变化）。</summary>
    public static readonly DependencyProperty ExpressionProperty =
        DependencyProperty.Register(
            nameof(Expression), typeof(BotExpression), typeof(BotView),
            new PropertyMetadata(BotExpression.Idle, OnExpressionChanged));

    /// <summary>瞬态情绪（插播一次后自动回落到当前稳态）。</summary>
    public static readonly DependencyProperty TransientExpressionProperty =
        DependencyProperty.Register(
            nameof(TransientExpression), typeof(BotExpression), typeof(BotView),
            new PropertyMetadata(BotExpression.Idle));

    /// <summary>
    /// 瞬态请求序号。每次自增代表「再播一次」，使同一情绪连续触发时也能重新播放
    /// （依赖属性值没变时不会触发变更回调）。
    /// </summary>
    public static readonly DependencyProperty TransientSequenceProperty =
        DependencyProperty.Register(
            nameof(TransientSequence), typeof(int), typeof(BotView),
            new PropertyMetadata(0, OnTransientSequenceChanged));

    /// <summary>设计稿的弹簧曲线：cubic-bezier(.34, 1.15, .5, 1)，y 超过 1 表示轻微过冲。</summary>
    private static readonly KeySpline SpringSpline = new(0.34, 1.15, 0.5, 1);

    private readonly DispatcherTimer _blinkTimer = new();
    private readonly DispatcherTimer _wanderTimer = new();
    private readonly DispatcherTimer _transientTimer = new();

    private bool _initialized;

    /// <summary>当前正在播放的瞬态；null = 没有插播。</summary>
    private BotExpression? _playingTransient;

    /// <summary>本轮瞬态回落使用的时长（眨眼/张望的回落比状态切换更快）。</summary>
    private int _returnDurationMs = BotExpressionCatalog.TRANSIENT_RETURN_MS;

    /// <summary>稳态情绪。</summary>
    public BotExpression Expression
    {
        get => (BotExpression)GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    /// <summary>要插播的瞬态情绪。</summary>
    public BotExpression TransientExpression
    {
        get => (BotExpression)GetValue(TransientExpressionProperty);
        set => SetValue(TransientExpressionProperty, value);
    }

    /// <summary>瞬态请求序号（自增即触发）。</summary>
    public int TransientSequence
    {
        get => (int)GetValue(TransientSequenceProperty);
        set => SetValue(TransientSequenceProperty, value);
    }

    /// <summary>构造控件并挂上三只计时器（常驻眨眼、常驻张望、瞬态回落）。</summary>
    public BotView()
    {
        InitializeComponent();

        _blinkTimer.Tick += OnBlinkTick;
        _wanderTimer.Tick += OnWanderTick;
        _transientTimer.Tick += OnTransientElapsed;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initialized = true;

        // 首帧直接到位，不从默认值动画过来，避免启动时看到一次无意义的形变。
        ApplySteady(Expression, animate: false);

        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        StartAmbient();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _initialized = false;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        StopAmbient();
        _transientTimer.Stop();
        _playingTransient = null;
    }

    // ---------- 依赖属性回调 ----------

    private static void OnExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (BotView)d;
        if (!view._initialized)
        {
            return;
        }

        // 瞬态播放期间不切稳态：播完时读的是当前 Expression，回退自然落到最新状态。
        if (view._playingTransient is not null)
        {
            return;
        }

        view.ApplySteady((BotExpression)e.NewValue, animate: true);
    }

    private static void OnTransientSequenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (BotView)d;
        if (!view._initialized || (int)e.NewValue <= 0)
        {
            return;
        }

        view.PlayTransient(view.TransientExpression);
    }

    // ---------- 应用预设 ----------

    /// <summary>按稳态情绪上屏。</summary>
    /// <param name="expression">稳态情绪。</param>
    /// <param name="animate">false = 直接到位（首帧 / 无障碍模式）。</param>
    /// <param name="durationMs">过渡时长；null 用设计稿的稳态切换时长。</param>
    private void ApplySteady(BotExpression expression, bool animate, int? durationMs = null) =>
        ApplyPreset(
            BotExpressionCatalog.Get(expression),
            durationMs ?? BotExpressionCatalog.STEADY_TRANSITION_MS,
            animate);

    /// <summary>把一条预设同时应用到左右眼、眼形层与光色。</summary>
    /// <param name="preset">渲染参数。</param>
    /// <param name="durationMs">眼部形变时长。</param>
    /// <param name="animate">false = 瞬切。</param>
    /// <param name="changeGlow">
    /// 是否同时应用光色。瞬态插播只改眼形与形变，沿用稳态的光色，
    /// 避免「琥珀临期时眨一下眼变冰白」这种闪烁。
    /// </param>
    private void ApplyPreset(BotExpressionPreset preset, int durationMs, bool animate, bool changeGlow = true)
    {
        ApplyEye(EyeScaleL, EyeRotateL, EyeMoveL, preset.Left, durationMs, animate);
        ApplyEye(EyeScaleR, EyeRotateR, EyeMoveR, preset.Right, durationMs, animate);
        ApplyShape(preset.Shape);

        if (changeGlow)
        {
            ApplyGlow(preset.Glow, animate);
        }
    }

    /// <summary>应用单只眼的平移、缩放、旋转。</summary>
    private static void ApplyEye(
        ScaleTransform scale,
        RotateTransform rotate,
        TranslateTransform move,
        EyeDeformation deformation,
        int durationMs,
        bool animate)
    {
        SetOrAnimate(scale, ScaleTransform.ScaleXProperty, deformation.ScaleX, durationMs, animate);
        SetOrAnimate(scale, ScaleTransform.ScaleYProperty, deformation.ScaleY, durationMs, animate);
        SetOrAnimate(rotate, RotateTransform.AngleProperty, deformation.RotateDegrees, durationMs, animate);
        SetOrAnimate(move, TranslateTransform.XProperty, deformation.TranslateX, durationMs, animate);
        SetOrAnimate(move, TranslateTransform.YProperty, deformation.TranslateY, durationMs, animate);
    }

    /// <summary>
    /// 双层动画：先是 0% 的离散关键帧锁住当前值，再用弹簧曲线走到目标值。
    /// <para>
    /// 不能直接 <c>new DoubleAnimation(to, duration)</c>：WPF 的缓动函数只有幂函数族，做不出
    /// cubic-bezier；而关键帧动画若没有 0% 帧，会从「基础值」起跳，连续切换时会看到回闪。
    /// </para>
    /// </summary>
    private static void SetOrAnimate(
        Animatable target,
        DependencyProperty property,
        double value,
        int durationMs,
        bool animate)
    {
        if (!animate || durationMs <= 0)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame((double)target.GetValue(property)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromPercent(1), SpringSpline));
        target.BeginAnimation(property, animation);
    }

    /// <summary>应用光色：眼睛、光晕与替代形状的发光同色，过渡 180ms。</summary>
    private void ApplyGlow(GlowTone tone, bool animate)
    {
        var color = ToColor(tone);
        var duration = BotExpressionCatalog.GLOW_TRANSITION_MS;

        SetOrAnimateColor(
            (SolidColorBrush)Resources["EyeBrush"],
            SolidColorBrush.ColorProperty,
            color,
            duration,
            animate);

        SetOrAnimateColor(
            (System.Windows.Media.Effects.DropShadowEffect)Resources["ShapeGlow"],
            System.Windows.Media.Effects.DropShadowEffect.ColorProperty,
            color,
            duration,
            animate);
    }

    /// <summary>颜色过渡；瞬切时清掉旧动画再写值，避免动画把直接赋值覆盖回去。</summary>
    private static void SetOrAnimateColor(
        Animatable target,
        DependencyProperty property,
        Color value,
        int durationMs,
        bool animate)
    {
        if (!animate || durationMs <= 0)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            return;
        }

        var from = (Color)target.GetValue(property);
        target.BeginAnimation(
            property,
            new ColorAnimation(from, value, TimeSpan.FromMilliseconds(durationMs)));
    }

    /// <summary>切换眼形层：同一时刻只让一组可见。</summary>
    private void ApplyShape(EyeShape shape)
    {
        var oval = shape == EyeShape.Oval;
        EyeGroupL.Visibility = oval ? Visibility.Visible : Visibility.Collapsed;
        EyeGroupR.Visibility = oval ? Visibility.Visible : Visibility.Collapsed;

        Arcs.Visibility = shape == EyeShape.Arc ? Visibility.Visible : Visibility.Collapsed;
        ArcsDeep.Visibility = shape == EyeShape.ArcDeep ? Visibility.Visible : Visibility.Collapsed;
        Sur.Visibility = shape == EyeShape.Circle ? Visibility.Visible : Visibility.Collapsed;
        Cross.Visibility = shape == EyeShape.Cross ? Visibility.Visible : Visibility.Collapsed;
        Excl.Visibility = shape == EyeShape.Exclamation ? Visibility.Visible : Visibility.Collapsed;
        Hearts.Visibility = shape == EyeShape.Heart ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>光色枚举 → 设计稿取值。</summary>
    private static Color ToColor(GlowTone tone) => tone switch
    {
        GlowTone.Amber => Color.FromRgb(0xFF, 0xC4, 0x6A),
        GlowTone.Mint => Color.FromRgb(0x7D, 0xEB, 0xAA),
        GlowTone.Coral => Color.FromRgb(0xFF, 0x6B, 0x57),
        GlowTone.Rose => Color.FromRgb(0xFF, 0x9D, 0xB0),
        _ => Color.FromRgb(0xCF, 0xE9, 0xFF),
    };

    // ---------- 瞬态播放 ----------

    /// <summary>
    /// 播放一次瞬态。会打断正在播放的瞬态；到保持时长后回落到<b>当前</b>稳态。
    /// </summary>
    /// <param name="expression">瞬态情绪；非瞬态或无障碍模式关闭时忽略。</param>
    private void PlayTransient(BotExpression expression)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var preset = BotExpressionCatalog.Get(expression);
        if (!preset.IsTransient)
        {
            return;
        }

        _transientTimer.Stop();
        _playingTransient = expression;

        // 眨眼/张望是「快速插播」，进出都比状态切换短；其余瞬态按状态切换节奏进场。
        var enterDuration = expression switch
        {
            BotExpression.Blink or BotExpression.Wink => BotExpressionCatalog.BLINK_CLOSE_MS,
            BotExpression.Wander => BotExpressionCatalog.WANDER_OUT_MS,
            _ => BotExpressionCatalog.STEADY_TRANSITION_MS,
        };
        _returnDurationMs = expression switch
        {
            BotExpression.Blink or BotExpression.Wink => BotExpressionCatalog.BLINK_OPEN_MS,
            BotExpression.Wander => BotExpressionCatalog.WANDER_BACK_MS,
            _ => BotExpressionCatalog.TRANSIENT_RETURN_MS,
        };

        // 瞬态只改眼形与形变，光色沿用稳态（瞬态不携带新光色）。
        ApplyPreset(preset, enterDuration, animate: true, changeGlow: false);

        _transientTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, preset.HoldMilliseconds));
        _transientTimer.Start();
    }

    private void OnTransientElapsed(object? sender, EventArgs e)
    {
        _transientTimer.Stop();
        _playingTransient = null;

        // 回退读的是当前稳态：插播期间课程状态可能已经变了，不能回到播放前的快照。
        ApplySteady(Expression, animate: true, _returnDurationMs);
    }

    // ---------- 常驻氛围：眨眼 + 张望 ----------

    private void StartAmbient()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        ScheduleBlink();
        ScheduleWander();
    }

    private void StopAmbient()
    {
        _blinkTimer.Stop();
        _wanderTimer.Stop();
    }

    private void ScheduleBlink()
    {
        _blinkTimer.Stop();
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(
            Random.Shared.Next(
                BotExpressionCatalog.BLINK_INTERVAL_MIN_MS,
                BotExpressionCatalog.BLINK_INTERVAL_MAX_MS + 1));
        _blinkTimer.Start();
    }

    private void ScheduleWander()
    {
        _wanderTimer.Stop();
        _wanderTimer.Interval = TimeSpan.FromMilliseconds(
            Random.Shared.Next(
                BotExpressionCatalog.WANDER_INTERVAL_MIN_MS,
                BotExpressionCatalog.WANDER_INTERVAL_MAX_MS + 1));
        _wanderTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        ScheduleBlink();
        TryPlayAmbient(BotExpression.Blink);
    }

    private void OnWanderTick(object? sender, EventArgs e)
    {
        ScheduleWander();
        TryPlayAmbient(BotExpression.Wander);
    }

    /// <summary>只在 idle 稳态、且当前没有插播时播放常驻瞬态。</summary>
    private void TryPlayAmbient(BotExpression expression)
    {
        if (_playingTransient is not null || Expression != BotExpression.Idle)
        {
            return;
        }

        PlayTransient(expression);
    }

    // ---------- 无障碍：跟随系统「动画效果」开关 ----------

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation))
        {
            return;
        }

        if (SystemParameters.ClientAreaAnimation)
        {
            StartAmbient();
        }
        else
        {
            StopAmbient();
            FinishTransientImmediately();
        }
    }

    /// <summary>关闭动画时把瞬态就地收掉，直接回到稳态。</summary>
    private void FinishTransientImmediately()
    {
        _transientTimer.Stop();
        if (_playingTransient is null)
        {
            return;
        }

        _playingTransient = null;
        ApplySteady(Expression, animate: false);
    }
}
