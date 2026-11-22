using System.IO;

namespace GameLauncher.Models;

/// <summary>
/// Decides whether a launch target is a supported URI protocol or a plain
/// executable path, and validates the target entered in the editor.
/// <para>
/// Only the protocols listed in <see cref="AllowedUriSchemes"/> are ever handed to
/// the shell. Anything else is rejected, so a library entry (or a malicious import)
/// cannot start an arbitrary protocol handler.
/// </para>
/// </summary>
public static class GameTarget
{
    /// <summary>
    /// URI protocols the launcher is allowed to open. Keep this list deliberately
    /// short: every entry is a protocol handler that can launch arbitrary software.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedUriSchemes = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        // The intended protocol.
        "steam",

        // Other PC launcher protocol handlers.
        "com.epicgames.launcher",
        "uplay",
        "origin",
        "battlenet",
        "goggalaxy"
    };

    /// <summary>
    /// True when <paramref name="target"/> is a URI whose scheme is on the allowlist.
    /// </summary>
    public static bool IsUri(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string trimmed = target.Trim();

        // Windows drive paths such as "C:\Games\demo.exe" are files, not URIs.
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || uri is null)
        {
            return false;
        }

        return AllowedUriSchemes.Contains(uri.Scheme);
    }

    /// <summary>
    /// True when <paramref name="target"/> is either a supported URI or a path that
    /// ends in <c>.exe</c>. Existence is checked at launch time, not here.
    /// </summary>
    public static bool IsValid(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string trimmed = target.Trim();

        if (IsUri(trimmed))
        {
            return true;
        }

        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A path must not be a URI in disguise.
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            // Reject paths with invalid characters / illegal syntax.
            _ = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            return false;
        }

        return true;
    }
}
