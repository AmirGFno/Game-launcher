using System.IO;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace GameLauncher.Services;

/// <summary>
/// ImageSharp-backed thumbnail cache with a bounded least-recently-used budget.
/// <para>
/// This is the successor of the v2.0.0 WPF <c>ImageLoader</c> cache: the same ideas
/// (fingerprint the source by path + write time + size, bound the total bytes, evict
/// the oldest) applied to files the WebView2 host can serve instead of bitmaps the
/// UI had to bind.
/// </para>
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    /// <summary>Upper bound on the thumbnail cache (256 MB).</summary>
    public const long MaxCacheBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Upper bound on a source image. Decoding is unbounded in ImageSharp, so a
    /// multi-hundred-megabyte file must be refused before it is loaded.
    /// </summary>
    public const long MaxSourceBytes = 64L * 1024 * 1024;

    private readonly string _cacheDirectory;

    public ThumbnailService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "Cache",
            "Thumbnails"))
    {
    }

    /// <param name="cacheDirectory">
    /// Test hook. Validated with <see cref="PathSafety.EnsureSafeRoot"/> so a test can
    /// point it at a temp folder without touching the real cache.
    /// </param>
    public ThumbnailService(string cacheDirectory)
    {
        _cacheDirectory = PathSafety.EnsureSafeRoot(cacheDirectory);
    }

    public string CacheDirectory => _cacheDirectory;

    public long CacheBytes
    {
        get
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return 0;
            }

            try
            {
                return Directory
                    .EnumerateFiles(_cacheDirectory, "*.png", SearchOption.TopDirectoryOnly)
                    .Sum(static path => new FileInfo(path).Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    public string? GetThumbnail(string? sourcePath, int maxDimension)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || maxDimension <= 0)
        {
            return null;
        }

        if (!File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            long length = new FileInfo(sourcePath).Length;
            if (length == 0 || length > MaxSourceBytes)
            {
                return null;
            }

            string key = BuildKey(sourcePath, length, File.GetLastWriteTimeUtc(sourcePath), maxDimension);
            string targetPath = Path.Combine(_cacheDirectory, key + ".png");

            if (File.Exists(targetPath))
            {
                // Refresh recency so an actively used thumbnail is not evicted.
                try
                {
                    File.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow);
                }
                catch
                {
                    // Cosmetic only.
                }

                return targetPath;
            }

            Directory.CreateDirectory(_cacheDirectory);

            using (Image image = Image.Load(sourcePath))
            {
                if (image.Width <= 0 || image.Height <= 0)
                {
                    return null;
                }

                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(maxDimension, maxDimension),
                    Mode = ResizeMode.Max
                }));

                image.SaveAsPng(targetPath);
            }

            TrimToBudget();
            return targetPath;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or UnknownImageFormatException
                                  or ImageProcessingException
                                  or NotSupportedException)
        {
            return null;
        }
    }

    public Task<string?> GetThumbnailAsync(
        string? sourcePath,
        int maxDimension,
        CancellationToken cancellationToken = default)
    {
        // Decoding and resizing are CPU work; keep them off the caller's thread.
        return Task.Run(() => GetThumbnail(sourcePath, maxDimension), cancellationToken);
    }

    public void TrimToBudget()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return;
        }

        FileInfo[] files;
        try
        {
            files = Directory
                .EnumerateFiles(_cacheDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .Select(static path => new FileInfo(path))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        long total = files.Sum(static file => file.Length);
        if (total <= MaxCacheBytes)
        {
            return;
        }

        // Oldest first: least-recently-used eviction.
        foreach (FileInfo file in files.OrderBy(static file => file.LastWriteTimeUtc))
        {
            if (total <= MaxCacheBytes)
            {
                break;
            }

            try
            {
                if (!PathSafety.IsSafeToDelete(file.FullName, _cacheDirectory))
                {
                    continue;
                }

                long length = file.Length;
                file.Delete();
                total -= length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked thumbnail - leave it for the next pass.
            }
        }
    }

    /// <summary>
    /// A key that changes whenever the source changes, so a replaced artwork is
    /// re-decoded instead of showing a stale thumbnail forever.
    /// </summary>
    private static string BuildKey(string sourcePath, long length, DateTime writeUtc, int maxDimension)
    {
        string raw = $"{maxDimension}|{Path.GetFullPath(sourcePath)}|{writeUtc.ToFileTimeUtc()}|{length}";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
