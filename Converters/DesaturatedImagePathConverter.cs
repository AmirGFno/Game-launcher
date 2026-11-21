using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameLauncher.Converters;

/// <summary>
/// Like <see cref="ImagePathConverter"/>, but returns a greyscale version so the
/// unselected carousel logos read as "background" next to the glowing selected one.
/// Falls back to the colour image if the pixel format conversion is unavailable.
/// </summary>
public sealed class DesaturatedImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var converter = new ImagePathConverter();
        if (converter.Convert(value, targetType, parameter, culture) is not BitmapSource source)
        {
            return null;
        }

        try
        {
            // FormatConvertedBitmap must be frozen before it crosses threads, and it
            // keeps the colour image alive as its source.
            var grey = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            grey.Freeze();
            return grey;
        }
        catch
        {
            // A pixel-format conversion problem must never break the carousel.
            return source;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
