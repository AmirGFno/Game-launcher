using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace GameLauncher.Services;

/// <summary>Shared bounded downloader. Expected failures are surfaced to editor/import callers, never reported as success.</summary>
public static class ImageDownloader
{
    public const long MaxBytes = 25L * 1024 * 1024;
    public const int MaxDimension = 16384;
    public const long MaxPixels = 64L * 1024 * 1024;
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    public const string Accept = "image/avif,image/webp,image/apng,image/*,*/*;q=0.8";
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
    public static string? CustomImagesDirectory { get; set; }
    public static string ImagesDirectory => CustomImagesDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "Images");

    public static Task<string> DownloadAsync(string? url, CancellationToken cancellationToken)
        => DownloadCoreAsync(Client, url, ImagesDirectory, null, cancellationToken);

    public static Task<string> DownloadStableAsync(string url, string stem, CancellationToken token)
        => DownloadCoreAsync(Client, url, ImagesDirectory, stem, token);

    // Injectable client and directory keep tests offline and isolated from real user files.
    internal static async Task<string> DownloadCoreAsync(HttpClient client, string? url, string directory, string? stem, CancellationToken token)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException("Enter a direct HTTP or HTTPS image URL.");
        if (stem is not null && (stem.Length > 100 || stem.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new ArgumentException("Invalid cache filename.");
        // Covers response headers AND streamed body; HttpClient's headers-only timeout alone is insufficient.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", Accept);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && (length < 12 || length > MaxBytes))
            throw new InvalidOperationException("Image response is empty, too small, or exceeds 25 MB.");
        directory = PathSafety.EnsureSafeRoot(directory);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > MaxBytes) throw new InvalidOperationException("Image exceeds the 25 MB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                if (total < 12) throw new InvalidOperationException("The image response was empty or incomplete.");
                await output.FlushAsync(token).ConfigureAwait(false);
            } // Critical: release the exclusive write handle BEFORE validating or moving the file.
            string extension = await Task.Run(() => ValidateAndGetExtension(temporary), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, (stem ?? Guid.NewGuid().ToString("N")) + extension);
            if (File.Exists(path) && PathSafety.IsReparsePoint(path)) throw new IOException("Cache target is a symbolic link.");
            if (File.Exists(path) && !File.Exists(path + ".downloaded"))
                throw new IOException("A manually added file already uses this artwork cache name.");
            File.Move(temporary, path, overwrite: true);
            GameLauncher.Converters.ImageLoader.Invalidate(path);
            // Explicit ownership marker: artwork clearing must not delete manually copied images.
            string marker = path + ".downloaded";
            if (!PathSafety.IsReparsePoint(marker)) File.WriteAllText(marker, "GFno downloaded artwork\n");
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Artwork download failed: " + ex.Message);
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static void ValidateImageFile(string path) => ValidateAndGetExtension(path);
    internal static string ValidateAndGetExtension(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidOperationException("Image exceeds the size limit.");
            using var probe = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            if (probe.Read(header) < 12) throw new InvalidOperationException("Incomplete image.");
            probe.Position = 0;
            // ICO is a native WPF format, not supported by ImageSharp 3.x.
            if (header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0)
            {
                var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames) CheckDimensions(frame.PixelWidth, frame.PixelHeight);
                if (decoder.Frames.Count == 0) throw new InvalidOperationException("Empty icon.");
                return ".ico";
            }
            var info = SixLabors.ImageSharp.Image.Identify(probe);
            CheckDimensions(info.Width, info.Height);
            probe.Position = 0;
            using var image = SixLabors.ImageSharp.Image.Load(probe);
            var format = image.Metadata.DecodedImageFormat?.Name.ToUpperInvariant();
            return format switch { "JPEG" => ".jpg", "WEBP" => ".webp", "PNG" => ".png", "GIF" => ".gif", "BMP" => ".bmp", "TIFF" => ".tiff", _ => throw new InvalidOperationException("Unsupported image format.") };
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException("The response is not a supported, decodable image.", ex); }
    }
    internal static void CheckDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidOperationException("Artwork dimensions exceed the safe decode limit.");
    }
}
