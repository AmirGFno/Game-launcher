namespace GameLauncher.Services;

/// <summary>
/// Produces sized thumbnail copies of artwork inside the application's own cache.
/// <para>
/// The frontend never loads a full-resolution background. It asks the backend for a
/// thumbnail, and the backend returns a file inside
/// <c>%LocalAppData%\GameLauncher\Cache\Thumbnails</c> that the WebView2 virtual
/// host can serve. Arbitrary user folders are therefore never exposed to the web
/// layer.
/// </para>
/// </summary>
public interface IThumbnailService
{
    /// <summary>The directory thumbnails are written to.</summary>
    string CacheDirectory { get; }

    /// <summary>Total size of the thumbnail cache in bytes.</summary>
    long CacheBytes { get; }

    /// <summary>
    /// Returns a thumbnail path for <paramref name="sourcePath"/> scaled so its
    /// longest edge is at most <paramref name="maxDimension"/> pixels, or
    /// <c>null</c> when the source is missing / unreadable / too large.
    /// </summary>
    string? GetThumbnail(string? sourcePath, int maxDimension);

    /// <summary>Async version; decoding happens off the calling thread.</summary>
    Task<string?> GetThumbnailAsync(
        string? sourcePath,
        int maxDimension,
        CancellationToken cancellationToken = default);

    /// <summary>Evicts the oldest thumbnails until the cache is back under budget.</summary>
    void TrimToBudget();
}
