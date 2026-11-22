using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Loads and stores user preferences as JSON inside the local AppData folder.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live settings file.</summary>
    string FilePath { get; }

    /// <summary>
    /// Loads the settings. A missing or unreadable file yields default settings;
    /// values outside their supported range are clamped by
    /// <see cref="AppSettings.Normalized"/>.
    /// </summary>
    AppSettings Load();

    /// <summary>Atomically persists the settings.</summary>
    Task SaveAsync(AppSettings settings);
}
