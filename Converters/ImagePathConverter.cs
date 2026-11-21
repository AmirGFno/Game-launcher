using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace GameLauncher.Converters;

/// <summary>
/// Converts an image file path to a <see cref="BitmapImage"/>.
/// Returns <c>null</c> for empty paths or files that do not exist / cannot be read.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return ImageLoader.Load(path);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
