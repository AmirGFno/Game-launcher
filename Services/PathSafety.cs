using System.IO;

namespace GameLauncher.Services;

/// <summary>
/// Canonical-path checks used before anything is deleted.
/// <para>
/// Deleting files is the one operation that must never be handed a user-supplied
/// path, so every path is expanded with <see cref="Path.GetFullPath"/>, compared
/// case-insensitively against the application-owned root, and rejected when it is a
/// drive root, a junction / symlink, or a well-known protected location.
/// </para>
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// True when <paramref name="fullPath"/> lies strictly inside
    /// <paramref name="root"/> (the root itself is not "inside" itself).
    /// </summary>
    public static bool IsWithin(string fullPath, string root)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string normalizedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (normalizedRoot.Length == 0)
        {
            return false;
        }

        return fullPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for <c>C:\</c>, <c>D:\</c>, ... .</summary>
    public static bool IsDriveRoot(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root)
            && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the path itself is a junction, mount point or symbolic link.
    /// Callers that recurse must also check every ancestor.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // Missing / inaccessible paths are treated as unsafe.
            return true;
        }
    }

    /// <summary>
    /// True when the path points into a Windows / program-files / personal-documents
    /// location that must never be touched by the launcher.
    /// </summary>
    public static bool IsProtectedLocation(string fullPath)
    {
        string[] protectedRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"),
            Path.GetTempPath()
        };

        foreach (string protectedRoot in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(protectedRoot))
            {
                continue;
            }

            try
            {
                string fullRoot = Path.GetFullPath(protectedRoot);
                if (IsWithin(fullPath, fullRoot) || string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore an unusable well-known folder.
            }
        }

        return false;
    }

    /// <summary>
    /// Validates a directory that the application intends to own and clean.
    /// Throws <see cref="ArgumentException"/> when the root is not safe.
    /// </summary>
    public static string EnsureSafeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("The cache root must not be empty.", nameof(root));
        }

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            throw new ArgumentException("The cache root is not a valid path.", nameof(root), ex);
        }

        if (IsDriveRoot(fullRoot))
        {
            throw new ArgumentException("The cache root must not be a drive root.", nameof(root));
        }

        if (IsProtectedLocation(fullRoot))
        {
            throw new ArgumentException("The cache root points at a protected system or user folder.", nameof(root));
        }

        if (IsReparsePoint(fullRoot))
        {
            throw new ArgumentException("The cache root must not be a junction or symbolic link.", nameof(root));
        }

        return fullRoot;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> may be deleted: it must be strictly
    /// inside <paramref name="allowedRoot"/>, and it must not be a drive root, a
    /// reparse point, or a protected location.
    /// </summary>
    public static bool IsSafeToDelete(string candidate, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate);
        }
        catch
        {
            return false;
        }

        string root;
        try
        {
            root = Path.GetFullPath(allowedRoot);
        }
        catch
        {
            return false;
        }

        if (IsDriveRoot(full))
        {
            return false;
        }

        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsWithin(full, root))
        {
            return false;
        }

        if (IsProtectedLocation(full))
        {
            return false;
        }

        if (IsReparsePoint(full))
        {
            return false;
        }

        return true;
    }
}
