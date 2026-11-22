using GameLauncher.Models;
using GameLauncher.NativeServices;
using GameLauncher.Performance;
using GameLauncher.Services;
using GameLauncher.WindowManagement;

namespace GameLauncher.Bridge;

/// <summary>
/// The services the bridge handlers use, wired up once in <c>App.xaml.cs</c>.
/// </summary>
public sealed class BridgeServices
{
    public BridgeServices(
        IGameLibraryService library,
        IGameLauncherService launcher,
        ITempCleanerService tempCleaner,
        IThumbnailService thumbnails,
        ISettingsService settings,
        IGameSessionHistoryService history,
        IProcessMonitorService monitor,
        IPerformanceRecommendationService recommendations,
        IStartupPreparationService preparation,
        INativeDialogService dialogs,
        WindowController window,
        string thumbnailBaseUrl,
        string appVersion)
    {
        Library = library;
        Launcher = launcher;
        TempCleaner = tempCleaner;
        Thumbnails = thumbnails;
        Settings = settings;
        History = history;
        Monitor = monitor;
        Recommendations = recommendations;
        Preparation = preparation;
        Dialogs = dialogs;
        Window = window;
        ThumbnailBaseUrl = thumbnailBaseUrl;
        AppVersion = appVersion;
    }

    public IGameLibraryService Library { get; }

    public IGameLauncherService Launcher { get; }

    public ITempCleanerService TempCleaner { get; }

    public IThumbnailService Thumbnails { get; }

    public ISettingsService Settings { get; }

    public IGameSessionHistoryService History { get; }

    public IProcessMonitorService Monitor { get; }

    public IPerformanceRecommendationService Recommendations { get; }

    public IStartupPreparationService Preparation { get; }

    public INativeDialogService Dialogs { get; }

    public WindowController Window { get; }

    /// <summary>
    /// Root URL the WebView2 virtual host serves the thumbnail cache from.
    /// </summary>
    public string ThumbnailBaseUrl { get; }

    public string AppVersion { get; }

    /// <summary>
    /// Last settings the frontend saved, kept in memory so a launch does not have to
    /// re-read the settings file. Refreshed by <c>settings.save</c>.
    /// </summary>
    public AppSettings SettingsSnapshot { get; set; } = new();

    /// <summary>
    /// Set by the desktop host so handlers can push backend-initiated events
    /// (<c>performance.updated</c>, <c>cache.progress</c>, ...).
    /// </summary>
    public Action<string, object?>? EventSink { get; set; }

    /// <summary>
    /// Set by the desktop host to apply settings it owns (tray behaviour, the
    /// per-user startup entry) after a successful <c>settings.save</c>.
    /// </summary>
    public Action<AppSettings>? SettingsApplied { get; set; }

    /// <summary>Pushes an event to the frontend. Safe to call when no sink is set.</summary>
    public void PublishEvent(string type, object? payload)
    {
        EventSink?.Invoke(type, payload);
    }
}
