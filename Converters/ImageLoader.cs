using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// Mutate/Resize are extension methods in SixLabors.ImageSharp.Processing, not
// instance members of Image - without this using the ImageSharp decode path
// fails with CS1061 ("'Image' does not contain a definition for 'Mutate'").
using SixLabors.ImageSharp.Processing;

namespace GameLauncher.Converters;

/// <summary>
/// Loads, downscales and caches the artwork used by the launcher.
/// <para>
/// Decoding runs on a worker thread (<see cref="LoadAsync"/>) so a multi-megapixel
/// background never freezes the UI. The cache is bounded (<see cref="MaxCacheBytes"/>)
/// and keyed by the source file's write time and length, so a changed file is
/// re-decoded automatically instead of showing a stale image forever.
/// </para>
/// </summary>
public static class ImageLoader
{
    /// <summary>Backgrounds are stretched over a 1080p screen at most.</summary>
    public const int MaxBackgroundWidth = 1920;

    /// <summary>Carousel logos are displayed at 120px; 256 keeps them crisp.</summary>
    public const int MaxLogoWidth = 256;

    /// <summary>Approximate memory budget for decoded bitmaps (256 MB).</summary>
    public const long MaxCacheBytes = 256L * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static long _cacheBytes;
    private static long _sequence;

    private sealed record CacheEntry(BitmapSource Image, long Bytes, long Sequence);

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

        string? fingerprint = Fingerprint(path);
        if (fingerprint is null)
        {
            return null;
        }

        string key = Key(path, maxWidth, desaturate, fingerprint);
        if (Cache.TryGetValue(key, out CacheEntry? cached))
        {
            return cached.Image;
        }

        BitmapSource? image = Decode(path, maxWidth, desaturate);
        if (image is not null)
        {
            Add(key, image);
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

        string? fingerprint = Fingerprint(path);
        if (fingerprint is null)
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        string key = Key(path, maxWidth, desaturate, fingerprint);
        if (Cache.TryGetValue(key, out CacheEntry? cached))
        {
            return Task.FromResult<BitmapSource?>(cached.Image);
        }

        return Task.Run(
            () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Cache.TryGetValue(key, out CacheEntry? hit))
                    {
                        return hit.Image;
                    }

                    BitmapSource? image = Decode(path, maxWidth, desaturate);
                    if (image is not null)
                    {
                        Add(key, image);
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

    /// <summary>
    /// Drops every cached variant of <paramref name="path"/>. Called after a file is
    /// overwritten so the next load cannot reuse a stale decode.
    /// </summary>
    public static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string suffix = "|" + path;
        foreach (string key in Cache.Keys.ToList())
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                Cache.TryRemove(key, out _);
            }
        }

        long total = 0;
        foreach (CacheEntry entry in Cache.Values)
        {
            total += entry.Bytes;
        }

        Interlocked.Exchange(ref _cacheBytes, total);
    }

    private static string Key(string path, int maxWidth, bool desaturate, string fingerprint)
        => $"{maxWidth}|{(desaturate ? 1 : 0)}|{fingerprint}|{path}";

    /// <summary>
    /// A cheap identity for the source file. Returning <c>null</c> means the file is
    /// missing or unreadable, so the caller must not cache anything.
    /// </summary>
    private static string? Fingerprint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        }
        catch
        {
            return null;
        }
    }

    private static void Add(string key, BitmapSource image)
    {
        long bytes = EstimateBytes(image);
        long sequence = Interlocked.Increment(ref _sequence);

        if (!Cache.TryAdd(key, new CacheEntry(image, bytes, sequence)))
        {
            return;
        }

        Interlocked.Add(ref _cacheBytes, bytes);
        EvictIfNeeded();
    }

    /// <summary>Removes the oldest entries until the cache is back under budget.</summary>
    private static void EvictIfNeeded()
    {
        while (Interlocked.Read(ref _cacheBytes) > MaxCacheBytes)
        {
            string? oldestKey = null;
            long oldestSequence = long.MaxValue;

            foreach (KeyValuePair<string, CacheEntry> pair in Cache)
            {
                if (pair.Value.Sequence < oldestSequence)
                {
                    oldestSequence = pair.Value.Sequence;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey is null)
            {
                break;
            }

            if (Cache.TryRemove(oldestKey, out CacheEntry? removed))
            {
                Interlocked.Add(ref _cacheBytes, -removed.Bytes);
            }
        }
    }

    private static long EstimateBytes(BitmapSource image)
        => (long)image.PixelWidth * image.PixelHeight * 4;

    private static BitmapSource? Decode(string path, int maxWidth, bool desaturate)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Content-based decoding keeps downloaded and manually renamed WebP consistent.
            if (new FileInfo(path).Length > 64L * 1024 * 1024) return null;
            // Sniffing is performed by ImageSharp, not the filename. A CDN may return WebP
            // behind a .jpg URL. ICO stays on the native WPF decoder.
            BitmapSource? bitmap = IsIcon(path)
                ? DecodeWithWpf(path, maxWidth)
                : DecodeWithImageSharp(path, maxWidth);

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
    /// Decodes content with ImageSharp and bridges resized BGRA pixels to a frozen WPF bitmap.
    /// The longest output edge is bounded by <paramref name="maxWidth"/>.
    /// </summary>
    private static BitmapSource? DecodeWithImageSharp(string path, int maxWidth)
    {
        try { return DecodeManaged(path, maxWidth); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Artwork decode failed: " + ex.Message);
            return null;
        }
    }

    // A direct pixel bridge avoids a second PNG encode/decode and WIC format negotiation.
    // Kept internal for deterministic regression testing; public loading still returns a placeholder on failure.
    internal static BitmapSource DecodeManaged(string path, int maxWidth)
    {
        var info = SixLabors.ImageSharp.Image.Identify(path);
        GameLauncher.Services.ImageDownloader.CheckDimensions(info.Width, info.Height);
        int limit = maxWidth > 0 ? maxWidth : MaxBackgroundWidth;
        var options = new SixLabors.ImageSharp.Formats.DecoderOptions {
            TargetSize = new SixLabors.ImageSharp.Size(limit, limit), MaxFrames = 1
        };
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(options, path);
        if (image.Width > limit || image.Height > limit)
            image.Mutate(context => context.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(limit,limit), Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
            }));
        byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
        image.CopyPixelDataTo(pixels);
        var bitmap = BitmapSource.Create(image.Width,image.Height,96,96,PixelFormats.Bgra32,null,pixels,image.Width*4);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsIcon(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        return stream.Read(magic) == 4 && magic[0] == 0 && magic[1] == 0 && magic[2] == 1 && magic[3] == 0;
    }
}
