using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using GameLauncher.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace GameLauncher.Models;

/// <summary>
/// How a game is started. This is the per-game launch profile the editor exposes
/// under "Performance"; <see cref="GameItem.LaunchTarget"/> stays the single source
/// of truth for *what* is launched (an <c>.exe</c> path or an allowlisted URI).
/// <para>
/// Every property has a safe default, so a <c>library.json</c> written by v2.0.0
/// (which has no launch profile at all) deserialises into a working profile and no
/// migration is needed.
/// </para>
/// </summary>
public sealed class LaunchProfile
{
    /// <summary>Default and recommended priority.</summary>
    public const string Normal = "Normal";

    /// <summary>Safe, higher-than-normal priority.</summary>
    public const string AboveNormal = "AboveNormal";

    /// <summary>High priority. Still safe: Realtime is never offered.</summary>
    public const string High = "High";

    /// <summary>Below-normal priority.</summary>
    public const string BelowNormal = "BelowNormal";

    /// <summary>Idle priority.</summary>
    public const string Idle = "Idle";

    /// <summary>
    /// The complete list of priorities the UI may offer. <c>Realtime</c> is
    /// deliberately absent: it can starve essential Windows components, so a game
    /// launcher must never set it.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedPriorities = new[]
    {
        Normal, AboveNormal, High, BelowNormal, Idle
    };

    /// <summary>
    /// True when <paramref name="priority"/> is one of <see cref="SupportedPriorities"/>.
    /// Anything else (including <c>Realtime</c>) is rejected.
    /// </summary>
    public static bool IsSupportedPriority(string? priority)
        => !string.IsNullOrWhiteSpace(priority)
           && SupportedPriorities.Contains(priority!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalised priority, falling back to <see cref="Normal"/>.</summary>
    public static string NormalizePriority(string? priority)
        => IsSupportedPriority(priority) ? priority! : Normal;

    /// <summary>Directory the game process starts in. Empty = the executable's folder.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Optional command-line arguments passed to the executable.</summary>
    public string CommandLineArguments { get; set; } = string.Empty;

    /// <summary>Process priority. Always one of <see cref="SupportedPriorities"/>.</summary>
    public string ProcessPriority { get; set; } = Normal;

    /// <summary>
    /// Optional image name of the real game process (for example <c>game.exe</c>) for
    /// URI-launched titles, where <c>Process.Start</c> only confirms that Steam received
    /// the request and no game process handle exists.
    /// </summary>
    public string? ProcessNameForMonitoring { get; set; }

    /// <summary>
    /// Applications the user explicitly configured to close gracefully before launching.
    /// Empty by default, and the whole feature stays off unless
    /// <see cref="SafeStartupPreparationEnabled"/> is set.
    /// </summary>
    public List<string> PreLaunchApplications { get; set; } = new();

    /// <summary>Per-game switch for Performance Center monitoring.</summary>
    public bool MonitoringEnabled { get; set; } = true;

    /// <summary>
    /// Opt-in safe startup preparation (validate target, check it is not already
    /// running, gracefully close the configured applications). Disabled by default.
    /// </summary>
    public bool SafeStartupPreparationEnabled { get; set; }

    /// <summary>Deep copy, so editing a game can never mutate the stored profile.</summary>
    public LaunchProfile Clone()
    {
        return new LaunchProfile
        {
            WorkingDirectory = WorkingDirectory,
            CommandLineArguments = CommandLineArguments,
            ProcessPriority = ProcessPriority,
            ProcessNameForMonitoring = ProcessNameForMonitoring,
            PreLaunchApplications = new List<string>(PreLaunchApplications),
            MonitoringEnabled = MonitoringEnabled,
            SafeStartupPreparationEnabled = SafeStartupPreparationEnabled
        };
    }
}

/// <summary>
/// A single entry of the user's game library.
/// <para>
/// The JSON shape is stable. v2.0.0 wrote
/// <c>Id, Title, LaunchTarget, LogoPath, BackgroundPath, CustomCacheFolderPath,
/// LastPlayed, LaunchCount, IsFavorite, Category, DominantColor,
/// DominantColorSource</c>; newer properties default safely when they are absent,
/// so an existing <c>library.json</c> loads unchanged.
/// </para>
/// </summary>
public sealed class GameItem : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the game.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Either a path to an executable (for example <c>C:\Games\Demo\demo.exe</c>)
    /// or a URI such as <c>steam://rungameid/12345</c>.
    /// </summary>
    public string LaunchTarget { get; set; } = string.Empty;

    /// <summary>Optional path to the game logo (.ico / .png / .jpg / .webp).</summary>
    public string? LogoPath { get; set; }

    /// <summary>Optional path to the full screen background image (.png / .jpg / .webp).</summary>
    public string? BackgroundPath { get; set; }

    /// <summary>
    /// Legacy per-game cache folder. It is preserved for backward compatibility but is
    /// <b>never</b> deleted automatically - cleanup only touches the application's own
    /// cache and must be started by the user.
    /// </summary>
    public string? CustomCacheFolderPath { get; set; }

    /// <summary>Timestamp of the last successful launch.</summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>How many times this game was launched successfully.</summary>
    public int LaunchCount { get; set; }

    /// <summary>User favourite, used by the library filters.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Optional user-defined category / collection.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// True when the launch target is a local executable that currently exists.
    /// URIs (steam://, ...) are always considered installed.
    /// </summary>
    [JsonIgnore]
    public bool IsInstalled => !GameTarget.IsUri(LaunchTarget) && File.Exists(LaunchTarget);

    /// <summary>
    /// Dominant logo colour as <c>#AARRGGBB</c>. Persisted so the glow does not have
    /// to be recomputed from scratch on every start; <see cref="DominantColorSource"/>
    /// records which logo it belongs to.
    /// </summary>
    public string? DominantColor { get; set; }

    /// <summary>Logo path the cached <see cref="DominantColor"/> was computed from.</summary>
    public string? DominantColorSource { get; set; }

    /// <summary>
    /// How the game is launched. Never <c>null</c>: a missing profile deserialises to
    /// defaults, so an old library keeps launching exactly as before.
    /// </summary>
    public LaunchProfile LaunchProfile { get; set; } = new();

    /// <summary>
    /// The colour that tints the selected tile's glow. It starts out as the default
    /// accent and is replaced on a background thread once the logo has been sampled.
    /// </summary>
    [JsonIgnore]
    public Color DominantColorValue { get; private set; } = ImageColorExtractor.DefaultAccent;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Caches an extracted colour and raises <see cref="PropertyChanged"/> so the
    /// glow binding picks it up without re-reading the image.
    /// </summary>
    public void SetDominantColor(Color color)
    {
        DominantColorValue = color;
        DominantColorSource = LogoPath;
        DominantColor = color.ToString();
        OnPropertyChanged(nameof(DominantColorValue));
    }

    /// <summary>
    /// Restores a previously persisted colour, but only when it still belongs to the
    /// current logo. Returns <c>true</c> when no extraction is needed.
    /// </summary>
    public bool RestoreDominantColor()
    {
        if (DominantColorSource != LogoPath || string.IsNullOrEmpty(DominantColor))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(DominantColor) is Color color)
            {
                DominantColorValue = color;
                OnPropertyChanged(nameof(DominantColorValue));
                return true;
            }
        }
        catch
        {
            // A malformed cached value simply means "extract it again".
        }

        return false;
    }

    /// <summary>
    /// Creates a shallow copy that keeps every persisted field - including fields added
    /// in future versions - so editing a game can never silently drop metadata.
    /// </summary>
    public GameItem Clone()
    {
        var clone = (GameItem)MemberwiseClone();
        clone.PropertyChanged = null;
        // Deep-copy the profile: an edit must never mutate the stored game.
        clone.LaunchProfile = LaunchProfile.Clone();
        return clone;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
