using System.IO;

namespace GameLauncher.Services;

/// <summary>
/// Recursively deletes the *contents* of the temporary folders.
/// Locked / in-use files are skipped instead of crashing the launcher.
/// </summary>
public sealed class TempCleanerService : ITempCleanerService
{
    private const string WindowsTempPath = @"C:\Windows\Temp";

    public Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<string> extraFolders,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        // The deletion is synchronous disk IO - keep it off the UI thread.
        return Task.Run(() => Clean(extraFolders, progress, cancellationToken), cancellationToken);
    }

    private static CleanupResult Clean(
        IReadOnlyCollection<string> extraFolders,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var folders = new List<string>
        {
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            WindowsTempPath
        };

        foreach (string folder in extraFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                folders.Add(folder.Trim());
            }
        }

        int deleted = 0;
        int skipped = 0;

        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                continue;
            }

            progress.Report($"Cleaning {Path.GetFileName(folder)}...");

            foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    FileAttributes attributes = File.GetAttributes(entry);

                    // Never follow junctions / symlinks - only skip them.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }

                    deleted++;
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
        }

        return new CleanupResult(deleted, skipped);
    }

    private static string BuildSummary(int deleted, int skipped)
    {
        string message = $"Removed {deleted} item(s)";
        return skipped > 0 ? $"{message}, skipped {skipped}." : $"{message}.";
    }
}
