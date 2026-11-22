namespace GameLauncher.Models;

/// <summary>
/// User preferences, persisted to <c>%LocalAppData%\GameLauncher\settings.json</c>.
/// <para>
/// Every entry maps to a real behaviour in the desktop host or the frontend, and
/// every property has a default so a missing or partial settings file still works.
/// </para>
/// </summary>
public sealed class AppSettings
{
    public const string ThemeMidnight = "midnight";
    public const string ThemeDeepNavy = "deep-navy";
    public const string ThemeGraphite = "graphite";

    public const string AnimationFull = "Full";
    public const string AnimationReduced = "Reduced";
    public const string AnimationNone = "None";

    public const string BlurNone = "None";
    public const string BlurLow = "Low";
    public const string BlurMedium = "Medium";
    public const string BlurHigh = "High";

    public const string ViewGrid = "grid";
    public const string ViewCarousel = "carousel";

    public const string SortManual = "manual";
    public const string SortTitle = "title";
    public const string SortRecent = "recent";
    public const string SortLaunchCount = "launchCount";

    /// <summary>Shortest allowed sample interval (ms).</summary>
    public const int MinSamplingIntervalMs = 500;

    /// <summary>Longest allowed sample interval (ms).</summary>
    public const int MaxSamplingIntervalMs = 5000;

    public static readonly IReadOnlyList<string> Themes = new[]
    {
        ThemeMidnight, ThemeDeepNavy, ThemeGraphite
    };

    public static readonly IReadOnlyList<string> AnimationIntensities = new[]
    {
        AnimationFull, AnimationReduced, AnimationNone
    };

    public static readonly IReadOnlyList<string> BlurIntensities = new[]
    {
        BlurNone, BlurLow, BlurMedium, BlurHigh
    };

    public static readonly IReadOnlyList<string> LibraryViews = new[] { ViewGrid, ViewCarousel };

    public static readonly IReadOnlyList<string> SortModes = new[]
    {
        SortManual, SortTitle, SortRecent, SortLaunchCount
    };

    /// <summary>Base theme. Affects the frontend CSS custom properties.</summary>
    public string Theme { get; set; } = ThemeMidnight;

    /// <summary>Accent colour as <c>#RRGGBB</c>.</summary>
    public string AccentColor { get; set; } = "#00D1B2";

    /// <summary>Secondary accent as <c>#RRGGBB</c>, used for gradients and glows.</summary>
    public string SecondaryAccent { get; set; } = "#E8A24C";

    /// <summary>Whether cinematic background crossfades are enabled.</summary>
    public bool AnimatedBackgrounds { get; set; } = true;

    /// <summary>One of <see cref="AnimationIntensities"/>.</summary>
    public string AnimationIntensity { get; set; } = AnimationFull;

    /// <summary>One of <see cref="BlurIntensities"/>.</summary>
    public string BlurIntensity { get; set; } = BlurMedium;

    /// <summary>Default library layout: <see cref="ViewGrid"/> or <see cref="ViewCarousel"/>.</summary>
    public string DefaultLibraryView { get; set; } = ViewGrid;

    /// <summary>Default library ordering: one of <see cref="SortModes"/>.</summary>
    public string DefaultSort { get; set; } = SortManual;

    /// <summary>Master switch for Performance Center monitoring.</summary>
    public bool MonitoringEnabled { get; set; } = true;

    /// <summary>Sample interval in milliseconds, clamped on load.</summary>
    public int SamplingIntervalMs { get; set; } = 1500;

    /// <summary>Minimise to the notification area instead of the taskbar.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Start with Windows. Implemented through the per-user
    /// <c>HKCU\...\Run</c> key, which needs no Administrator rights.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Returns a copy with every value inside its supported range.</summary>
    public AppSettings Normalized()
    {
        return new AppSettings
        {
            Theme = Themes.Contains(Theme, StringComparer.Ordinal) ? Theme : ThemeMidnight,
            AccentColor = IsValidHex(AccentColor) ? AccentColor : "#00D1B2",
            SecondaryAccent = IsValidHex(SecondaryAccent) ? SecondaryAccent : "#E8A24C",
            AnimatedBackgrounds = AnimatedBackgrounds,
            AnimationIntensity = AnimationIntensities.Contains(AnimationIntensity, StringComparer.Ordinal)
                ? AnimationIntensity
                : AnimationFull,
            BlurIntensity = BlurIntensities.Contains(BlurIntensity, StringComparer.Ordinal)
                ? BlurIntensity
                : BlurMedium,
            DefaultLibraryView = LibraryViews.Contains(DefaultLibraryView, StringComparer.Ordinal)
                ? DefaultLibraryView
                : ViewGrid,
            DefaultSort = SortModes.Contains(DefaultSort, StringComparer.Ordinal) ? DefaultSort : SortManual,
            MonitoringEnabled = MonitoringEnabled,
            SamplingIntervalMs = Math.Clamp(SamplingIntervalMs, MinSamplingIntervalMs, MaxSamplingIntervalMs),
            MinimizeToTray = MinimizeToTray,
            StartWithWindows = StartWithWindows
        };
    }

    /// <summary>True for a <c>#RRGGBB</c> or <c>#AARRGGBB</c> colour.</summary>
    public static bool IsValidHex(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '#')
        {
            return false;
        }

        if (value.Length is not (7 or 9))
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
