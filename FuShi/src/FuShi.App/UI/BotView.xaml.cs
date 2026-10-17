using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FuShi.UI;

/// <summary>团子的情绪态。光色只给状态；瞬态由调用方负责回落。</summary>
public enum BotMood { Idle, Focus, Soon, Done, Alert }

/// <summary>
/// 团 · 黑团子：亮面黑壳体 + 两只竖椭圆发光眼。
/// 无嘴无眉无腮红；情绪全部由双眼完成。
/// 动效原则：静止为主，偶发眨眼/张望插播；瞬态必回退；禁止常驻呼吸。
/// </summary>
public partial class BotView : UserControl
{
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(BotMood), typeof(BotView),
            new PropertyMetadata(BotMood.Idle));

    private static readonly Color Ice   = Color.FromRgb(0xCF, 0xE9, 0xFF);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xC4, 0x6A);
    private static readonly Color Mint  = Color.FromRgb(0x7D, 0xEB, 0xAA);
    private static readonly Color Coral = Color.FromRgb(0xFF, 0x6B, 0x5E);

    private readonly DispatcherTimer _blinkTimer = new();
    private readonly DispatcherTimer _glanceTimer = new();
    private readonly Random _rand = new();
    private bool _initialized;

    public BotMood Mood
    {
        get => (BotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public BotView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _initialized = true; ApplyMood(); StartAmbient(); };
        Unloaded += (_, _) => StopAmbient();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == MoodProperty && _initialized) ApplyMood();
    }

    // ---------- 情绪 → 眼形 / 光色 ----------
    private void ApplyMood()
    {
        var glow = Mood switch
        {
            BotMood.Soon  => Amber,
            BotMood.Done  => Mint,
            BotMood.Alert => Coral,
            _             => Ice,
        };

        var eye = (SolidColorBrush)Resources["EyeBrush"];
        eye.Color = glow;
        ((DropShadowEffect)Resources["EyeGlow"]).Color = glow;
        Aura.Fill = new RadialGradientBrush(
            Color.FromArgb(52, glow.R, glow.G, glow.B),
            Color.FromArgb(0, glow.R, glow.G, glow.B));

        bool arcs = Mood == BotMood.Done;
        EyeL.Visibility = arcs ? Visibility.Collapsed : Visibility.Visible;
        EyeR.Visibility = arcs ? Visibility.Collapsed : Visibility.Visible;
        ArcL.Visibility = arcs ? Visibility.Visible : Visibility.Collapsed;
        ArcR.Visibility = arcs ? Visibility.Visible : Visibility.Collapsed;

        double sx = Mood == BotMood.Soon ? 1.12 : 1.0;
        double sy = Mood == BotMood.Focus ? 0.58 : sx;
        AnimateEyes(sx, sy);
    }

    private void AnimateEyes(double x, double y)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var ax = new DoubleAnimation(x, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        var ay = new DoubleAnimation(y, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        EyeScaleL.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
        EyeScaleL.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        EyeScaleR.BeginAnimation(ScaleTransform.ScaleXProperty, ax.Clone());
        EyeScaleR.BeginAnimation(ScaleTransform.ScaleYProperty, ay.Clone());
    }

    // ---------- 常驻氛围：偶发眨眼 + 偶发张望（均必回位） ----------
    private void StartAmbient()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(3200 + _rand.Next(4200));
        _blinkTimer.Tick += (_, _) => { Blink(); _blinkTimer.Interval = TimeSpan.FromMilliseconds(3200 + _rand.Next(4200)); };
        _blinkTimer.Start();

        _glanceTimer.Interval = TimeSpan.FromMilliseconds(9000 + _rand.Next(9000));
        _glanceTimer.Tick += (_, _) => { Glance(); _glanceTimer.Interval = TimeSpan.FromMilliseconds(9000 + _rand.Next(9000)); };
        _glanceTimer.Start();
    }

    private void StopAmbient()
    {
        _blinkTimer.Stop();
        _glanceTimer.Stop();
    }

    private void Blink()
    {
        var down = new DoubleAnimation(1, 0.08, TimeSpan.FromMilliseconds(70)) { EasingFunction = new QuadraticEase() };
        down.Completed += (_, _) =>
        {
            var up = new DoubleAnimation(0.08, 1, TimeSpan.FromMilliseconds(100)) { EasingFunction = new QuadraticEase() };
            BlinkScale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        };
        BlinkScale.BeginAnimation(ScaleTransform.ScaleYProperty, down);

        // 四分之一概率连眨两次，更像"活物"
        if (_rand.Next(4) == 0)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            t.Tick += (_, _) => { ((DispatcherTimer)t).Stop(); Blink(); };
            t.Start();
        }
    }

    private void Glance()
    {
        // 专注态不东张西望
        if (Mood is BotMood.Focus or BotMood.Alert) return;

        double dx = (_rand.Next(2) == 0 ? -1 : 1) * (2.0 + _rand.NextDouble() * 2.6);
        var go = new DoubleAnimation(dx, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        go.Completed += (_, _) =>
        {
            var back = new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(420))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            GlanceTx.BeginAnimation(TranslateTransform.XProperty, back);
        };
        GlanceTx.BeginAnimation(TranslateTransform.XProperty, go);
    }
}
