namespace GameLauncher.Models;

/// <summary>
/// A single entry of the user's game library.
/// </summary>
public sealed class GameItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the game.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Either a path to an executable (for example <c>C:\Games\Demo\demo.exe</c>)
    /// or a URI such as <c>steam://rungameid/12345</c>.
    /// </summary>
    public string LaunchTarget { get; set; } = string.Empty;

    /// <summary>Optional path to the game logo (.ico / .png / .jpg).</summary>
    public string? LogoPath { get; set; }

    /// <summary>Optional path to the full screen background image (.png / .jpg).</summary>
    public string? BackgroundPath { get; set; }

    /// <summary>Optional folder whose contents are deleted before every launch.</summary>
    public string? CustomCacheFolderPath { get; set; }

    /// <summary>Timestamp of the last successful launch.</summary>
    public DateTime? LastPlayed { get; set; }
}
