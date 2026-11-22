using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameLauncher.Converters;

/// <summary>Converts <c>true</c> to <see cref="Visibility.Visible"/> and <c>false</c> to Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
