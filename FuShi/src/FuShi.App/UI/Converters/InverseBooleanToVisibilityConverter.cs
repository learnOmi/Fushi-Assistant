using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FuShi.UI.Converters;

/// <summary>
/// 布尔取反 → 可见性：当值为 false 时可见、true 时折叠。
/// 用于「空态提示」与「内容区」互斥显示（空课表时显示提示、隐藏网格）。
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>false → Visible；true → Collapsed。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>反向转换不实现（单向绑定使用）。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("InverseBooleanToVisibilityConverter 仅支持单向绑定");
}
