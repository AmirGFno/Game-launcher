namespace GameLauncher.Services;

/// <summary>Summary of a temp-cleanup run.</summary>
public sealed record CleanupResult(int Deleted, int Skipped);

/// <summary>
/// Deletes temporary files before a game is launched.
/// </summary>
public interface ITempCleanerService
{
    /// <summary>
    /// Clears <c>%TEMP%</c>, <c>C:\Windows\Temp</c> and every folder supplied in
    /// <paramref name="extraFolders"/> (for example a per-game cache folder).
    /// </summary>
    Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<string> extraFolders,
        IProgress<string> progress,
        CancellationToken cancellationToken);
}
