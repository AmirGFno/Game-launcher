using System.IO;
using System.Net.Http;

namespace GameLauncher.Services;

/// <summary>
/// Downloads an image from a direct URL into the local image cache, so the
/// downloaded artwork survives even if the remote file disappears.
/// <para>
/// Downloads are capped (<see cref="MaxBytes"/>), the bytes are streamed to a
/// temporary file, and the result is validated as a real, decodable image before it
/// is moved into place. An interrupted or oversized download never leaves a partial
/// file behind.
/// </para>
/// </summary>
public static class ImageDownloader
{
    /// <summary>Maximum accepted download size (25 MB).</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>Maximum accepted image dimension in either direction.</summary>
    public const int MaxDimension = 16384;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Test hook: redirects the download directory. Production leaves this null and
    /// uses <c>%LocalAppData%\GameLauncher\Images</c>.
    /// </summary>
    public static string? CustomImagesDirectory { get; set; }

    private static string ImagesDirectory => CustomImagesDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLauncher",
        "Images");

    /// <summary>
    /// Downloads <paramref name="url"/> and stores it under the image cache.
    /// Returns the saved file path.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid http(s) URL.</exception>
    /// <exception cref="HttpRequestException">The server returned an error status.</exception>
    /// <exception cref="InvalidOperationException">
    /// The response was too large, empty, or not a usable image.
    /// </exception>
    public static async Task<string> DownloadAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Paste an image URL into the field first.", nameof(url));
        }

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("That is not a valid http(s) image URL.");
        }

        using HttpResponseMessage response = await Client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declaredLength && declaredLength > MaxBytes)
        {
            throw new InvalidOperationException(
                $"The image is larger than the {MaxBytes / (1024 * 1024)} MB limit.");
        }

        string extension = ResolveExtension(response, uri);

        Directory.CreateDirectory(ImagesDirectory);
        string targetPath = Path.Combine(ImagesDirectory, $"{Guid.NewGuid():N}{extension}");
        string temporaryPath = targetPath + ".part";

        try
        {
            await using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            await using FileStream target = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            byte[] buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;

                if (total > MaxBytes)
                {
                    throw new InvalidOperationException(
                        $"The image is larger than the {MaxBytes / (1024 * 1024)} MB limit.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
            }

            if (total == 0)
            {
                throw new InvalidOperationException("The download was empty.");
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Only accept a real, decodable image within sane dimensions.
            ValidateImageFile(temporaryPath);

            File.Move(temporaryPath, targetPath);
            return targetPath;
        }
        catch
        {
            // Never leave a partial download behind.
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> unless the file is a decodable
    /// image within <see cref="MaxDimension"/>. Internal so the tests can exercise it
    /// without a network call.
    /// </summary>
    internal static void ValidateImageFile(string path)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(path);

            if (image.Width <= 0 || image.Height <= 0)
            {
                throw new InvalidOperationException("The image has no usable dimensions.");
            }

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                throw new InvalidOperationException(
                    $"The image is larger than {MaxDimension}x{MaxDimension} pixels.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException("The downloaded file is not a valid image.", ex);
        }
        catch
        {
            throw new InvalidOperationException("The downloaded file is not a valid image.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort - the .part file is harmless.
        }
    }

    /// <summary>Picks a file extension from the content type, falling back to the URL.</summary>
    private static string ResolveExtension(HttpResponseMessage response, Uri uri)
    {
        string? contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            switch (contentType.ToLowerInvariant())
            {
                case "image/png":
                    return ".png";
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                case "image/gif":
                    return ".gif";
                case "image/bmp":
                    return ".bmp";
                case "image/tiff":
                    return ".tiff";
                case "image/webp":
                    return ".webp";
                case "image/x-icon":
                case "image/icon":
                case "image/vnd.microsoft.icon":
                    return ".ico";
            }
        }

        string pathExtension = Path.GetExtension(uri.AbsolutePath);
        if (pathExtension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" or ".webp" or ".ico")
        {
            return pathExtension == ".jpeg" ? ".jpg" : pathExtension;
        }

        // Unknown content type: WPF can still show PNG/JPEG, so default to .png.
        return ".png";
    }
}
