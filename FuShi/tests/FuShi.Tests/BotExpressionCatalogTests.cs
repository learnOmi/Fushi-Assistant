using FluentAssertions;
using FuShi.Domain.Assistant;

namespace FuShi.Tests;

/// <summary>
/// 表情预设表的单测：19 种齐全、瞬态归类正确、关键数值与设计稿一致。
/// <para>
/// 数值断言只挑有代表性的几条（闭眼、放大、压扁、位移、旋转），不逐字段复述常量；
/// 目的是拦住「改表时手滑」和「枚举与表不同步」两类错误。
/// </para>
/// </summary>
public sealed class BotExpressionCatalogTests
{
    [Fact]
    public void All_ShouldContainExactlyNineteenExpressions()
    {
        var declared = Enum.GetValues<BotExpression>();

        declared.Should().HaveCount(19);
        BotExpressionCatalog.All.Should().HaveCount(declared.Length);
        BotExpressionCatalog.All.Select(preset => preset.Expression)
            .Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void Get_ShouldThrow_WhenExpressionHasNoPreset()
    {
        var unknown = (BotExpression)999;

        var act = () => BotExpressionCatalog.Get(unknown);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TransientSet_ShouldMatchDesignGroups()
    {
        var transients = BotExpressionCatalog.All
            .Where(preset => preset.IsTransient)
            .Select(preset => preset.Expression);

        transients.Should().BeEquivalentTo(new[]
        {
            BotExpression.Blink,
            BotExpression.Wander,
            BotExpression.Wink,
            BotExpression.Laugh,
            BotExpression.Surprise,
            BotExpression.Dizzy,
        });
    }

    [Fact]
    public void SteadyPresets_ShouldHaveZeroHold_AndTransientsPositiveHold()
    {
        foreach (var preset in BotExpressionCatalog.All)
        {
            if (preset.IsTransient)
            {
                preset.HoldMilliseconds.Should().BePositive(
                    $"{preset.Expression} 是瞬态，必须有回落时长");
            }
            else
            {
                preset.HoldMilliseconds.Should().Be(0,
                    $"{preset.Expression} 是稳态，由信号变化驱动而非计时");
            }
        }
    }

    [Theory]
    [InlineData(BotExpression.Soon, GlowTone.Amber)]
    [InlineData(BotExpression.Done, GlowTone.Mint)]
    [InlineData(BotExpression.Late, GlowTone.Coral)]
    [InlineData(BotExpression.Love, GlowTone.Rose)]
    [InlineData(BotExpression.Focus, GlowTone.Ice)]
    [InlineData(BotExpression.Sleepy, GlowTone.Ice)]
    public void Glow_ShouldFollowStatusOnly(BotExpression expression, GlowTone expected)
    {
        BotExpressionCatalog.Get(expression).Glow.Should().Be(expected);
    }

    [Theory]
    [InlineData(BotExpression.Done, EyeShape.Arc)]
    [InlineData(BotExpression.Laugh, EyeShape.ArcDeep)]
    [InlineData(BotExpression.Surprise, EyeShape.Circle)]
    [InlineData(BotExpression.Dizzy, EyeShape.Cross)]
    [InlineData(BotExpression.Late, EyeShape.Exclamation)]
    [InlineData(BotExpression.Love, EyeShape.Heart)]
    [InlineData(BotExpression.Focus, EyeShape.Oval)]
    public void Shape_ShouldMatchDesign(BotExpression expression, EyeShape expected)
    {
        BotExpressionCatalog.Get(expression).Shape.Should().Be(expected);
    }

    [Theory]
    [InlineData(BotExpression.Blink, 0.08)]
    [InlineData(BotExpression.Focus, 0.58)]
    [InlineData(BotExpression.Break, 0.10)]
    [InlineData(BotExpression.Sleepy, 0.30)]
    [InlineData(BotExpression.Shy, 0.90)]
    [InlineData(BotExpression.Sad, 0.60)]
    public void ScaleY_ShouldMatchDesign(BotExpression expression, double expected)
    {
        var preset = BotExpressionCatalog.Get(expression);

        preset.Left.ScaleY.Should().Be(expected);
        preset.Right.ScaleY.Should().Be(expected);
    }

    [Fact]
    public void Soon_ShouldEnlargeBothEyes()
    {
        var preset = BotExpressionCatalog.Get(BotExpression.Soon);

        preset.Left.Should().Be(new EyeDeformation(ScaleX: 1.1, ScaleY: 1.1));
        preset.Right.Should().Be(preset.Left);
    }

    [Fact]
    public void Wander_ShouldShiftEyesLeftAndUp()
    {
        BotExpressionCatalog.Get(BotExpression.Wander).Left
            .Should().Be(new EyeDeformation(TranslateX: -3.5, TranslateY: -1));
    }

    [Fact]
    public void Sleepy_ShouldDropEyelids()
    {
        BotExpressionCatalog.Get(BotExpression.Sleepy).Left
            .Should().Be(new EyeDeformation(ScaleY: 0.30, TranslateY: 4));
    }

    [Fact]
    public void Wink_ShouldCloseLeftEyeOnly()
    {
        var preset = BotExpressionCatalog.Get(BotExpression.Wink);

        preset.Left.ScaleY.Should().Be(0.08);
        preset.Right.Should().Be(EyeDeformation.Identity);
    }

    [Fact]
    public void Smug_ShouldTiltEyesInward()
    {
        var preset = BotExpressionCatalog.Get(BotExpression.Smug);

        preset.Left.Should().Be(new EyeDeformation(ScaleY: 0.52, RotateDegrees: -8));
        preset.Right.Should().Be(new EyeDeformation(ScaleY: 0.52, RotateDegrees: 8));
    }

    [Fact]
    public void Angry_ShouldTiltEyesInwardWithOppositeSigns()
    {
        var preset = BotExpressionCatalog.Get(BotExpression.Angry);

        preset.Left.Should().Be(new EyeDeformation(ScaleY: 0.6, TranslateY: 1, RotateDegrees: 13));
        preset.Right.Should().Be(new EyeDeformation(ScaleY: 0.6, TranslateY: 1, RotateDegrees: -13));
    }

    [Theory]
    [InlineData(BotExpression.Blink, 80)]
    [InlineData(BotExpression.Wander, 600)]
    [InlineData(BotExpression.Laugh, 900)]
    [InlineData(BotExpression.Surprise, 1200)]
    [InlineData(BotExpression.Dizzy, 900)]
    [InlineData(BotExpression.Wink, 600)]
    public void TransientHold_ShouldMatchDesignOrInferredRhythm(BotExpression expression, int expected)
    {
        BotExpressionCatalog.Get(expression).HoldMilliseconds.Should().Be(expected);
    }
}
