using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Windows.Media;
using GameLauncher.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace GameLauncher.Models;

/// <summary>
/// A single entry of the user's game library.
/// </summary>
public sealed class GameItem : INotifyPropertyChanged
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

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
        clone.AdditionalData = AdditionalData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        return clone;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
