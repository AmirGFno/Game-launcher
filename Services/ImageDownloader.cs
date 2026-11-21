using System.IO;
using System.Net.Http;

namespace GameLauncher.Services;

/// <summary>
/// Downloads an image from a direct URL into the local image cache, so the
/// downloaded artwork survives even if the remote file disappears.
/// </summary>
public static class ImageDownloader
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string ImagesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLauncher",
        "Images");

    /// <summary>
    /// Downloads <paramref name="url"/> and stores it under
    /// <c>%LocalAppData%\GameLauncher\Images</c>. Returns the saved file path.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid http(s) URL.</exception>
    /// <exception cref="HttpRequestException">The server returned an error status.</exception>
    /// <exception cref="InvalidOperationException">The response was not a usable image.</exception>
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

        string extension = ResolveExtension(response, uri);

        Directory.CreateDirectory(ImagesDirectory);
        string targetPath = Path.Combine(ImagesDirectory, $"{Guid.NewGuid():N}{extension}");

        await using Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using FileStream target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

        return targetPath;
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
