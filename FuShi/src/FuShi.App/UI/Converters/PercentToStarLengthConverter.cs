using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FuShi.UI.Converters;

/// <summary>
/// 百分比（0~100）→ <see cref="GridLength"/>（星号单位）。
/// 用于进度条：把「当前进度百分比」映射成填充列的星号权重，剩余列同理，
/// 免去在视图里按像素宽度做数学，且窗口宽度自适应。
/// </summary>
internal sealed class PercentToStarLengthConverter : IValueConverter
{
    /// <summary>百分比下限。</summary>
    private const double MIN_PERCENT = 0.0;

    /// <summary>百分比上限。</summary>
    private const double MAX_PERCENT = 100.0;

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double raw ? raw : MIN_PERCENT;
        percent = Math.Clamp(percent, MIN_PERCENT, MAX_PERCENT);
        return new GridLength(percent, GridUnitType.Star);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
