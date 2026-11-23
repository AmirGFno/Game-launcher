namespace GameLauncher.Services;

/// <summary>What a cleanup run would remove, without touching anything.</summary>
public sealed record CleanupPreview(int EntryCount, long TotalBytes);

/// <summary>Summary of an optional cleanup run.</summary>
public sealed record CleanupResult(int Deleted, int Skipped, long FreedBytes);

/// <summary>
/// Optional, explicitly initiated cleanup of the application's own cache folder.
/// <para>
/// It never touches <c>%TEMP%</c>, <c>C:\Windows\Temp</c> or a per-game folder, and
/// pre-launch cleanup is disabled unless the user explicitly enables it.
/// </para>
/// </summary>
public interface ITempCleanerService
{
    /// <summary>The application-owned cache directory that may be cleaned.</summary>
    string CacheDirectory { get; }

    /// <summary>Lists the entries that a clean would remove (cancellable).</summary>
    Task<CleanupPreview> PreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes the cached entries (cancellable, explicitly initiated).</summary>
    Task<CleanupResult> CleanAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken = default);
}
