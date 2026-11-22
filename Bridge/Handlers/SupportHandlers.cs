using System.Diagnostics;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Performance;
using GameLauncher.Services;
using GameLauncher.Utilities;
using GameLauncher.WebView;

namespace GameLauncher.Bridge.Handlers;

// ===========================================================================
// Settings
// ===========================================================================

/// <summary>settings.get</summary>
public sealed class SettingsGetHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public SettingsGetHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.SettingsGet;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(new { settings = _services.Settings.Load() });
    }
}

/// <summary>settings.save - persists and refreshes the in-memory snapshot.</summary>
public sealed class SettingsSaveHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public SettingsSaveHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.SettingsSave;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        AppSettings? incoming = request.Payload.GetObject<AppSettings>("settings");
        if (incoming is null)
        {
            throw new BridgeHandlerException("The request did not contain settings.");
        }

        // Normalized() clamps every value to a supported range before it is stored.
        await _services.Settings.SaveAsync(incoming).ConfigureAwait(false);
        _services.SettingsSnapshot = incoming.Normalized();

        try
        {
            _services.SettingsApplied?.Invoke(_services.SettingsSnapshot);
        }
        catch
        {
            // A desktop-host side effect must never fail the save.
        }

        return new { settings = _services.SettingsSnapshot };
    }
}

// ===========================================================================
// Artwork
// ===========================================================================

/// <summary>
/// image.download - downloads an image into the app-owned cache and optionally
/// assigns it to a game.
/// </summary>
public sealed class ImageDownloadHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public ImageDownloadHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.ImageDownload;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? url = request.Payload.GetString("url");
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new BridgeHandlerException("No image URL was supplied.");
        }

        string path = await ImageDownloader.DownloadAsync(url, cancellationToken).ConfigureAwait(false);

        Guid? gameId = request.Payload.GetGuid("gameId");
        string? field = request.Payload.GetString("field");

        if (gameId is null || string.IsNullOrEmpty(field))
        {
            return new { path };
        }

        List<GameItem> games = (await _services.Library.LoadAsync().ConfigureAwait(false)).ToList();
        GameItem? game = games.FirstOrDefault(candidate => candidate.Id == gameId.Value);
        if (game is null)
        {
            return new { path };
        }

        if (string.Equals(field, "logo", StringComparison.OrdinalIgnoreCase))
        {
            game.LogoPath = path;
        }
        else if (string.Equals(field, "background", StringComparison.OrdinalIgnoreCase))
        {
            game.BackgroundPath = path;
        }

        await _services.Library.SaveAsync(games).ConfigureAwait(false);

        return new { path, game, games };
    }
}

/// <summary>
/// image.thumbnail - returns a sized thumbnail URL served by the WebView2 host.
/// </summary>
public sealed class ImageThumbnailHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public ImageThumbnailHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.ImageThumbnail;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? path = request.Payload.GetString("path");
        int maxDimension = request.Payload.GetInt32("maxDimension", 512);

        string? thumbnail = await _services.Thumbnails
            .GetThumbnailAsync(path, Math.Clamp(maxDimension, 16, 2560), cancellationToken)
            .ConfigureAwait(false);

        if (thumbnail is null)
        {
            return new { url = (string?)null, path };
        }

        string url = _services.ThumbnailBaseUrl + "/" + Path.GetFileName(thumbnail);

        return new { url, path = thumbnail };
    }
}

/// <summary>image.dominantColor - the colour used for the selected tile's glow.</summary>
public sealed class ImageDominantColorHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public ImageDominantColorHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.ImageDominantColor;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? path = request.Payload.GetString("path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return new { color = ImageColorExtractor.DefaultAccent };
        }

        string color = (await ImageColorExtractor.ExtractDominantColorAsync(path)
            .ConfigureAwait(false)).ToString();

        Guid? gameId = request.Payload.GetGuid("gameId");
        if (gameId is not null)
        {
            List<GameItem> games = (await _services.Library.LoadAsync().ConfigureAwait(false)).ToList();
            GameItem? game = games.FirstOrDefault(candidate => candidate.Id == gameId.Value);
            if (game is not null)
            {
                game.DominantColor = color;
                game.DominantColorSource = path;
                await _services.Library.SaveAsync(games).ConfigureAwait(false);
            }
        }

        return new { color };
    }
}

// ===========================================================================
// Performance Center
// ===========================================================================

/// <summary>performance.status - everything the Performance Center renders.</summary>
public sealed class PerformanceStatusHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public PerformanceStatusHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.PerformanceStatus;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");

        IReadOnlyList<GameItem> games = await _services.Library.LoadAsync().ConfigureAwait(false);
        GameItem? game = id is null ? null : games.FirstOrDefault(candidate => candidate.Id == id.Value);

        SystemPerformanceSnapshot snapshot = _services.Recommendations.GetSystemSnapshot();

        IReadOnlyList<PerformanceRecommendation> recommendations = game is null
            ? _services.Recommendations.AnalyzeSystem()
            : _services.Recommendations.Analyze(game, _services.Monitor.Status, _services.Monitor.CurrentSample);

        IReadOnlyList<GameSessionRecord> history = _services.History.Load();

        return new
        {
            session = _services.Monitor.Status,
            sample = _services.Monitor.CurrentSample,
            snapshot,
            recommendations,
            history
        };
    }
}

/// <summary>
/// performance.start - begins monitoring a game the user says is already running.
/// For an .exe target the process is located by image name; for a URI target the
/// configured process name is watched for.
/// </summary>
public sealed class PerformanceStartHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public PerformanceStartHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.PerformanceStart;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");
        if (id is null)
        {
            throw new BridgeHandlerException("The request did not contain a game id.");
        }

        IReadOnlyList<GameItem> games = await _services.Library.LoadAsync().ConfigureAwait(false);
        GameItem? game = games.FirstOrDefault(candidate => candidate.Id == id.Value);
        if (game is null)
        {
            throw new BridgeHandlerException("That game is no longer in the library.");
        }

        LaunchProfile profile = game.LaunchProfile ?? new LaunchProfile();
        DateTime requestedAt = DateTime.UtcNow;

        int? processId = null;
        DateTime? detectedAt = null;

        if (!GameTarget.IsUri(game.LaunchTarget))
        {
            string imageName = Path.GetFileNameWithoutExtension(game.LaunchTarget);
            Process? running = FindRunning(imageName);
            if (running is not null)
            {
                processId = running.Id;
                detectedAt = DateTime.UtcNow;
                running.Dispose();
            }
        }

        var outcome = new LaunchOutcome
        {
            Success = true,
            Message = "Monitoring started.",
            GameId = game.Id,
            LaunchTarget = game.LaunchTarget,
            IsUri = GameTarget.IsUri(game.LaunchTarget),
            ProcessId = processId,
            ProcessName = profile.ProcessNameForMonitoring,
            LaunchRequestedAtUtc = requestedAt,
            GameProcessDetectedAtUtc = detectedAt
        };

        _services.Monitor.Start(
            game,
            outcome,
            TimeSpan.FromMilliseconds(_services.SettingsSnapshot.SamplingIntervalMs));

        return new { session = _services.Monitor.Status };
    }

    private static Process? FindRunning(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return null;
        }

        foreach (Process process in Process.GetProcessesByName(imageName))
        {
            try
            {
                if (!process.HasExited)
                {
                    return process;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process vanished.
            }
        }

        return null;
    }
}

/// <summary>performance.stop - stops the sampler and records the session length.</summary>
public sealed class PerformanceStopHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public PerformanceStopHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.PerformanceStop;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        LaunchSessionInfo status = _services.Monitor.Stop();

        if (status.State == MonitoringState.Ended && status.SessionStartedAtUtc is not null)
        {
            await _services.History.RecordAsync(new GameSessionRecord(
                status.GameId,
                status.GameTitle,
                status.LaunchRequestedAtUtc,
                status.SessionDuration,
                status.Error is null,
                status.Error)).ConfigureAwait(false);
        }

        return new { session = status };
    }
}

/// <summary>performance.recommendations - measured, explained advice.</summary>
public sealed class PerformanceRecommendationsHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public PerformanceRecommendationsHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.PerformanceRecommendations;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");

        if (id is null)
        {
            return new { recommendations = _services.Recommendations.AnalyzeSystem() };
        }

        IReadOnlyList<GameItem> games = await _services.Library.LoadAsync().ConfigureAwait(false);
        GameItem? game = games.FirstOrDefault(candidate => candidate.Id == id.Value);
        if (game is null)
        {
            throw new BridgeHandlerException("That game is no longer in the library.");
        }

        IReadOnlyList<PerformanceRecommendation> recommendations = _services.Recommendations.Analyze(
            game,
            _services.Monitor.Status,
            _services.Monitor.CurrentSample);

        return new { recommendations };
    }
}

// ===========================================================================
// Cache
// ===========================================================================

/// <summary>cache.preview - what a clean would remove, without touching anything.</summary>
public sealed class CachePreviewHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public CachePreviewHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.CachePreview;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        CleanupPreview preview = await _services.TempCleaner.PreviewAsync(cancellationToken)
                                                 .ConfigureAwait(false);

        return new
        {
            entryCount = preview.EntryCount,
            totalBytes = preview.TotalBytes,
            path = _services.TempCleaner.CacheDirectory,
            thumbnailCacheBytes = _services.Thumbnails.CacheBytes
        };
    }
}

/// <summary>
/// cache.clean - explicit, user-initiated, app-owned cache cleanup only.
/// Progress is pushed as <c>cache.progress</c> events.
/// </summary>
public sealed class CacheCleanHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public CacheCleanHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.CacheClean;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var progress = new Progress<string>(message =>
        {
            _services.PublishEvent("cache.progress", new { message });
        });

        CleanupResult result = await _services.TempCleaner
            .CleanAsync(progress, cancellationToken)
            .ConfigureAwait(false);

        // Keep the thumbnail cache inside its own budget too.
        _services.Thumbnails.TrimToBudget();

        return new
        {
            deleted = result.Deleted,
            skipped = result.Skipped,
            freedBytes = result.FreedBytes,
            thumbnailCacheBytes = _services.Thumbnails.CacheBytes
        };
    }
}

// ===========================================================================
// Window
// ===========================================================================

/// <summary>Shared behaviour for the window commands.</summary>
public abstract class WindowCommandHandler : IBridgeHandler
{
    protected WindowCommandHandler(BridgeServices services) => Services = services;

    protected BridgeServices Services { get; }

    public abstract string Type { get; }

    public abstract Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken);

    protected Task<object?> State()
    {
        return Task.FromResult<object?>(new { state = Services.Window.State.ToString() });
    }
}

public sealed class WindowMinimizeHandler : WindowCommandHandler
{
    public WindowMinimizeHandler(BridgeServices services) : base(services) { }

    public override string Type => BridgeContract.WindowMinimize;

    public override Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Services.Window.Minimize();
        return State();
    }
}

public sealed class WindowMaximizeHandler : WindowCommandHandler
{
    public WindowMaximizeHandler(BridgeServices services) : base(services) { }

    public override string Type => BridgeContract.WindowMaximize;

    public override Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Services.Window.MaximizeOrRestore();
        return State();
    }
}

public sealed class WindowCloseHandler : WindowCommandHandler
{
    public WindowCloseHandler(BridgeServices services) : base(services) { }

    public override string Type => BridgeContract.WindowClose;

    public override Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Services.Window.Close();
        return State();
    }
}

public sealed class WindowSetFullscreenHandler : WindowCommandHandler
{
    public WindowSetFullscreenHandler(BridgeServices services) : base(services) { }

    public override string Type => BridgeContract.WindowSetFullscreen;

    public override Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        bool fullscreen = request.Payload.GetBoolean("fullscreen", fallback: true);
        Services.Window.SetFullscreen(fullscreen);
        return State();
    }
}

// ===========================================================================
// Native dialogs, system, application
// ===========================================================================

/// <summary>dialog.openFile - native file picker.</summary>
public sealed class DialogOpenFileHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public DialogOpenFileHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.DialogOpenFile;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? filter = request.Payload.GetString("filter");
        string? title = request.Payload.GetString("title");

        string? path = _services.Dialogs.OpenFile(
            string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter,
            title);

        return Task.FromResult<object?>(new { path });
    }
}

/// <summary>dialog.openFolder - native folder picker.</summary>
public sealed class DialogOpenFolderHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public DialogOpenFolderHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.DialogOpenFolder;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? path = _services.Dialogs.OpenFolder(request.Payload.GetString("description"));
        return Task.FromResult<object?>(new { path });
    }
}

/// <summary>system.openPath - opens a folder or file with the Windows shell.</summary>
public sealed class SystemOpenPathHandler : IBridgeHandler
{
    public SystemOpenPathHandler(BridgeServices services)
    {
    }

    public string Type => BridgeContract.SystemOpenPath;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? path = request.Payload.GetString("path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BridgeHandlerException("No path was supplied.");
        }

        bool opened = File.Exists(path) || Directory.Exists(path)
            ? SystemIntegration.OpenFolderInExplorer(path)
            : SystemIntegration.OpenPath(path);

        return Task.FromResult<object?>(new { opened });
    }
}

/// <summary>app.version - version and bridge metadata.</summary>
public sealed class AppVersionHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public AppVersionHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.AppVersion;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(new
        {
            version = _services.AppVersion,
            product = "GFno Launcher",
            bridgeVersion = BridgeContract.Version
        });
    }
}

/// <summary>app.runtimeStatus - whether the WebView2 Runtime is present.</summary>
public sealed class AppRuntimeStatusHandler : IBridgeHandler
{
    public AppRuntimeStatusHandler(BridgeServices services)
    {
    }

    public string Type => BridgeContract.AppRuntimeStatus;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        WebView2RuntimeInfo info = WebView2RuntimeChecker.Check();

        return Task.FromResult<object?>(new
        {
            isInstalled = info.IsInstalled,
            version = info.Version,
            downloadUrl = WebView2RuntimeInfo.OfficialDownloadUrl
        });
    }
}
