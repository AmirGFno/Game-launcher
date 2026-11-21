using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.Services;

namespace GameLauncher.Converters;

/// <summary>
/// Turns a logo path into the dominant colour of that logo. Used to tint the
/// <see cref="System.Windows.Media.DropShadowEffect.Color"/> of the selected
/// carousel tile, so the glow matches the artwork. Falls back to the extractor's
/// default accent colour.
/// </summary>
public sealed class DominantColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return ImageColorExtractor.DefaultAccent;
        }

        // Small decode (logos are capped at 256px), and it warms the cache used by
        // the carousel tile itself.
        BitmapImage? image = ImageLoader.Load(path, ImageLoader.MaxLogoWidth);
        return image is null
            ? ImageColorExtractor.DefaultAccent
            : ImageColorExtractor.ExtractDominantColor(image);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
