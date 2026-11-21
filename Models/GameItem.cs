using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using GameLauncher.Services;
using Color = System.Windows.Media.Color;

namespace GameLauncher.Models;

/// <summary>
/// A single entry of the user's game library.
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

    /// <summary>Optional folder whose contents are deleted before every launch.</summary>
    public string? CustomCacheFolderPath { get; set; }

    /// <summary>Timestamp of the last successful launch.</summary>
    public DateTime? LastPlayed { get; set; }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
