using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.Converters;
using Color = System.Windows.Media.Color;

namespace GameLauncher.Services;

/// <summary>
/// Finds the most dominant "colourful" colour of a game logo. The result tints the
/// soft glow that surrounds the selected carousel tile, so the accent colour always
/// matches the artwork instead of being a fixed brand colour.
/// </summary>
public static class ImageColorExtractor
{
    /// <summary>Used when nothing usable can be sampled from the image.</summary>
    public static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x70, 0xD1);

    /// <summary>
    /// Perceived brightness (0-255) below which a pixel is considered background
    /// (black / very dark) and is skipped, so dark logos do not produce a black glow.
    /// </summary>
    private const double BrightnessThreshold = 50.0;

    /// <summary>Bucket size used to quantise similar colours together.</summary>
    private const int QuantizationStep = 16;

    /// <summary>Rough upper bound on the number of pixels inspected per image.</summary>
    private const int MaxSamplePixels = 64 * 64;

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct CacheEntry(Color Color, DateTime WriteUtc, long Length);

    /// <summary>
    /// Returns the dominant colour of <paramref name="image"/>, or
    /// <see cref="DefaultAccent"/> when the image is empty / unreadable.
    /// </summary>
    public static Color ExtractDominantColor(BitmapSource image)
    {
        if (image is null)
        {
            return DefaultAccent;
        }

        try
        {
            // Only file-backed bitmaps can be cached by path; a derived image (the
            // desaturated carousel variant) is simply re-extracted.
            if (image is BitmapImage { UriSource: { IsFile: true } uri })
            {
                string path = uri.LocalPath;
                if (Cache.TryGetValue(path, out CacheEntry cached) && IsCurrent(path, cached))
                {
                    return cached.Color;
                }

                Color color = Extract(image);
                Cache[path] = new CacheEntry(color, SafeWriteUtc(path), SafeLength(path));
                return color;
            }

            return Extract(image);
        }
        catch
        {
            // Colour extraction is purely cosmetic - it must never break the carousel.
            return DefaultAccent;
        }
    }

    /// <summary>
    /// Decodes the logo off the UI thread and returns its dominant colour. Both the
    /// decode (<see cref="ImageLoader"/>) and the pixel sampling are cached, so this
    /// is cheap to call repeatedly.
    /// </summary>
    public static async Task<Color> ExtractDominantColorAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DefaultAccent;
        }

        BitmapSource? image = await ImageLoader
            .LoadAsync(path, ImageLoader.MaxLogoWidth, desaturate: false, cancellationToken)
            .ConfigureAwait(false);

        if (image is null)
        {
            return DefaultAccent;
        }

        // CopyPixels and the bucketing are pure CPU work - keep them off the UI thread.
        return await Task.Run(() => ExtractDominantColor(image), cancellationToken)
                          .ConfigureAwait(false);
    }

    private static Color Extract(BitmapSource image)
    {
        int width = image.PixelWidth;
        int height = image.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return DefaultAccent;
        }

        // Convert first so the sampling code never has to care about the file's
        // pixel format (PNG, JPEG, BMP, ...). A frozen BitmapImage is readable.
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        int step = Math.Max(1, (int)Math.Sqrt((double)width * height / MaxSamplePixels));
        var buckets = new Dictionary<int, Bucket>();

        for (int y = 0; y < height; y += step)
        {
            int row = y * stride;
            for (int x = 0; x < width; x += step)
            {
                int i = row + (x * 4);
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                // Ignore fully transparent pixels (the logo's background).
                if (a < 32)
                {
                    continue;
                }

                // Ignore black / dark grey pixels so the glow is never muddy.
                double brightness = (0.299 * r) + (0.587 * g) + (0.114 * b);
                if (brightness < BrightnessThreshold)
                {
                    continue;
                }

                int key = ((r / QuantizationStep) << 16)
                        | ((g / QuantizationStep) << 8)
                        | (b / QuantizationStep);

                if (!buckets.TryGetValue(key, out Bucket? bucket))
                {
                    bucket = new Bucket();
                    buckets[key] = bucket;
                }

                bucket.Add(r, g, b);
            }
        }

        if (buckets.Count == 0)
        {
            return DefaultAccent;
        }

        Bucket winner = buckets.Values.OrderByDescending(static b => b.Count).First();
        return winner.ToColor();
    }

    private static bool IsCurrent(string path, CacheEntry cached)
    {
        try
        {
            return File.Exists(path)
                && Math.Abs((File.GetLastWriteTimeUtc(path) - cached.WriteUtc).TotalSeconds) < 1.0
                && new FileInfo(path).Length == cached.Length;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime SafeWriteUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Accumulates the pixels that fell into one quantised colour bucket.</summary>
    private sealed class Bucket
    {
        private long _r;
        private long _g;
        private long _b;

        public int Count { get; private set; }

        public void Add(byte r, byte g, byte b)
        {
            _r += r;
            _g += g;
            _b += b;
            Count++;
        }

        public Color ToColor()
        {
            int count = Count == 0 ? 1 : Count;
            return Color.FromRgb(
                (byte)Math.Clamp(_r / count, 0, 255),
                (byte)Math.Clamp(_g / count, 0, 255),
                (byte)Math.Clamp(_b / count, 0, 255));
        }
    }
}
