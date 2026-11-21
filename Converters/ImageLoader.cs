using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameLauncher.Converters;

/// <summary>
/// Loads, downscales and caches the artwork used by the launcher.
/// <para>
/// Decoding runs on a worker thread (<see cref="LoadAsync"/>) so a multi-megapixel
/// background never freezes the UI, and every decoded image is kept in memory so
/// navigating back to an already seen game is a cheap dictionary lookup.
/// </para>
/// </summary>
public static class ImageLoader
{
    /// <summary>Backgrounds are stretched over a 1080p screen at most.</summary>
    public const int MaxBackgroundWidth = 1920;

    /// <summary>Carousel logos are displayed at 120px; 256 keeps them crisp.</summary>
    public const int MaxLogoWidth = 256;

    private static readonly ConcurrentDictionary<string, BitmapSource> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Synchronous, cached load. Used by value converters, which run on the UI
    /// thread: once the cache is warm this is a dictionary read, and the decode
    /// itself is downscaled, so it stays cheap.
    /// </summary>
    public static BitmapSource? Load(string? path, int maxWidth = 0, bool desaturate = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string key = Key(path, maxWidth, desaturate);
        if (Cache.TryGetValue(key, out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource? image = Decode(path, maxWidth, desaturate);
        if (image is not null)
        {
            Cache.TryAdd(key, image);
        }

        return image;
    }

    /// <summary>
    /// Asynchronous, cached, downscaled load. The file is read and decoded on a
    /// thread-pool thread; the returned image is frozen and safe to use on the UI
    /// thread.
    /// </summary>
    public static Task<BitmapSource?> LoadAsync(
        string? path,
        int maxWidth = 0,
        bool desaturate = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        string key = Key(path, maxWidth, desaturate);
        if (Cache.TryGetValue(key, out BitmapSource? cached))
        {
            return Task.FromResult(cached);
        }

        return Task.Run(
            () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Cache.TryGetValue(key, out BitmapSource? hit))
                    {
                        return hit;
                    }

                    BitmapSource? image = Decode(path, maxWidth, desaturate);
                    if (image is not null)
                    {
                        Cache.TryAdd(key, image);
                    }

                    return image;
                }
                catch
                {
                    // Never let a bad path or a broken image fault the task.
                    return null;
                }
            },
            cancellationToken);
    }

    private static string Key(string path, int maxWidth, bool desaturate)
        => $"{maxWidth}|{(desaturate ? 1 : 0)}|{path}";

    private static BitmapSource? Decode(string path, int maxWidth, bool desaturate)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // WebP is not supported by WPF; everything else goes through WPF, with
            // ImageSharp as a safety net for formats WPF refuses.
            BitmapImage? bitmap = IsWebp(path)
                ? DecodeWithImageSharp(path, maxWidth)
                : DecodeWithWpf(path, maxWidth) ?? DecodeWithImageSharp(path, maxWidth);

            if (bitmap is null)
            {
                return null;
            }

            bitmap.Freeze();

            if (!desaturate)
            {
                return bitmap;
            }

            // Greyscale copy for the unselected carousel tiles.
            var grey = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
            grey.Freeze();
            return grey;
        }
        catch
        {
            // A broken or unsupported image must never take the launcher down.
            return null;
        }
    }

    private static BitmapImage? DecodeWithWpf(string path, int maxWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // OnLoad releases the file handle immediately and lets us freeze the
            // result, so the image can cross threads.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (maxWidth > 0)
            {
                // Decode straight into the target size: a 4K wallpaper never exists
                // in memory at full resolution.
                bitmap.DecodePixelWidth = maxWidth;
            }

            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes with ImageSharp (WebP and any other format WPF cannot handle) and
    /// re-encodes to PNG in memory, which WPF then decodes at the requested size.
    /// </summary>
    private static BitmapImage? DecodeWithImageSharp(string path, int maxWidth)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(path);
            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (maxWidth > 0)
            {
                bitmap.DecodePixelWidth = maxWidth;
            }

            bitmap.StreamSource = stream;
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWebp(string path)
        => string.Equals(
            System.IO.Path.GetExtension(path),
            ".webp",
            StringComparison.OrdinalIgnoreCase);
}
