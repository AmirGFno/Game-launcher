namespace GameLauncher.Models;

/// <summary>
/// Helpers that decide whether a launch target is a URI (steam://, https://, ...)
/// or a plain file path, and validate the target entered in the editor.
/// </summary>
public static class GameTarget
{
    public static bool IsUri(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (target.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        // Do not treat Windows drive paths such as "C:\Games\demo.exe" as a URI.
        if (target.Length >= 2 && char.IsLetter(target[0]) && target[1] == ':')
        {
            return false;
        }

        return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
               && uri is not null
               && uri.Scheme.Length > 1
               && !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A launch target is valid when it is a URI or points to an .exe file.
    /// (The file itself may be installed later, so existence is checked at launch time.)
    /// </summary>
    public static bool IsValid(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var trimmed = target.Trim();
        return IsUri(trimmed) || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
