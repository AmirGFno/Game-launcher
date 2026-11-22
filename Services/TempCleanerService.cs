using System.IO;

namespace GameLauncher.Services;

/// <summary>
/// Cleans a single, application-owned cache directory:
/// <c>%LocalAppData%\GameLauncher\Cache</c>.
/// <para>
/// Every entry is validated through <see cref="PathSafety"/> before it is deleted:
/// canonical paths only, no drive roots, no junctions / symlinks, no protected
/// system or personal folders. Locked files are skipped instead of throwing, and the
/// whole operation is cancellable.
/// </para>
/// </summary>
public sealed class TempCleanerService : ITempCleanerService
{
    private readonly string _cacheDirectory;

    public TempCleanerService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "Cache"))
    {
    }

    /// <param name="cacheDirectory">
    /// The directory this instance is allowed to clean. It is validated with
    /// <see cref="PathSafety.EnsureSafeRoot"/>, so a test can point it at a temp
    /// folder without touching the real application cache.
    /// </param>
    public TempCleanerService(string cacheDirectory)
    {
        _cacheDirectory = PathSafety.EnsureSafeRoot(cacheDirectory);
    }

    public string CacheDirectory => _cacheDirectory;

    public Task<CleanupPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        // Pure disk inspection - keep it off the UI thread.
        return Task.Run(() => Preview(cancellationToken), cancellationToken);
    }

    public Task<CleanupResult> CleanAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return Task.Run(() => Clean(progress, cancellationToken), cancellationToken);
    }

    private CleanupPreview Preview(CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        int count = 0;

        foreach (string entry in EnumerateSafeEntries(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalBytes += SafeGetSize(entry);
            count++;
        }

        return new CleanupPreview(count, totalBytes);
    }

    private CleanupResult Clean(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        int deleted = 0;
        int skipped = 0;
        long freedBytes = 0;

        if (!Directory.Exists(_cacheDirectory))
        {
            progress.Report("Nothing to clean.");
            return new CleanupResult(0, 0, 0);
        }

        progress.Report($"Cleaning {Path.GetFileName(_cacheDirectory)}...");

        // Materialise first: deleting while enumerating a directory is undefined.
        foreach (string entry in EnumerateSafeEntries(cancellationToken).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                long size = SafeGetSize(entry);

                if (DeleteEntry(entry))
                {
                    deleted++;
                    freedBytes += size;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or System.Security.SecurityException)
            {
                // File is in use or protected - skip it and keep going.
                skipped++;
            }
        }

        progress.Report(BuildSummary(deleted, skipped));
        return new CleanupResult(deleted, skipped, freedBytes);
    }

    /// <summary>
    /// Direct children of the cache root that pass every safety check. Nothing is
    /// ever followed through a junction or symbolic link.
    /// </summary>
    private IEnumerable<string> EnumerateSafeEntries(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            yield break;
        }

        // The root itself must not be a link, otherwise every "child" is suspect.
        if (PathSafety.IsReparsePoint(_cacheDirectory))
        {
            yield break;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(_cacheDirectory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (PathSafety.IsSafeToDelete(entry, _cacheDirectory))
            {
                yield return entry;
            }
        }
    }

    private static bool DeleteEntry(string entry)
    {
        FileAttributes attributes = File.GetAttributes(entry);

        // Defence in depth: never follow a link that appeared after enumeration.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(entry, recursive: true);
        }
        else
        {
            File.Delete(entry);
        }

        return true;
    }

    private static long SafeGetSize(string entry)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                return Directory
                    .EnumerateFileSystemEntries(entry, "*", SearchOption.AllDirectories)
                    .Where(File.Exists)
                    .Sum(file => new FileInfo(file).Length);
            }

            return new FileInfo(entry).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string BuildSummary(int deleted, int skipped)
    {
        string message = $"Removed {deleted} item(s)";
        return skipped > 0 ? $"{message}, skipped {skipped}." : $"{message}.";
    }
}
