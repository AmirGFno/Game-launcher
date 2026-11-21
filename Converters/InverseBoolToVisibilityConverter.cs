using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameLauncher.Converters;

/// <summary>Converts <c>false</c> to <see cref="Visibility.Visible"/> and <c>true</c> to Collapsed.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
