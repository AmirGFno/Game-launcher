using GameLauncher.Models;
using GameLauncher.Performance;
using GameLauncher.Services;

namespace GameLauncher.Bridge.Handlers;

/// <summary>
/// game.launch - the primary action.
/// <para>
/// Order matters and is deliberately minimal so a launch feels instant:
/// optional opt-in preparation, launch, record history, start monitoring. Nothing
/// else runs before the process is started.
/// </para>
/// </summary>
public sealed class GameLaunchHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public GameLaunchHandler(BridgeServices services) => _services = services;

    private readonly Dictionary<Guid, DateTime> _recentRequests = new();

    public string Type => BridgeContract.GameLaunch;

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

        var session = _services.Monitor.Status;
        if (session.GameId == game.Id && session.State is MonitoringState.Running or MonitoringState.WaitingForProcess)
            throw new BridgeHandlerException("This game is already running or waiting for its process.");
        if (_recentRequests.TryGetValue(game.Id, out DateTime previous)
            && DateTime.UtcNow - previous < TimeSpan.FromSeconds(5))
            throw new BridgeHandlerException("A launch was just requested. Please wait before trying again.");
        _recentRequests[game.Id] = DateTime.UtcNow;
        foreach (Guid stale in _recentRequests.Where(pair => DateTime.UtcNow - pair.Value > TimeSpan.FromMinutes(1)).Select(pair => pair.Key).ToArray())
            _recentRequests.Remove(stale);

        LaunchProfile profile = game.LaunchProfile ?? new LaunchProfile();

        // Safe startup preparation is opt-in per game and off by default.
        if (profile.SafeStartupPreparationEnabled)
        {
            PreparationResult preparation = _services.Preparation.Prepare(game);
            if (!preparation.CanLaunch)
            {
                throw new BridgeHandlerException(
                    "Safe startup preparation did not pass: "
                    + string.Join("; ", preparation.Failed.Select(static check => check.Message)));
            }

            if (profile.PreLaunchApplications.Count > 0)
            {
                var progress = new Progress<string>(_ => { });
                await _services.Preparation
                    .CloseApplicationsAsync(profile.PreLaunchApplications, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        LaunchOutcome outcome = _services.Launcher.Launch(game);

        if (outcome.Success)
        {
            game.LastPlayed = outcome.LaunchRequestedAtUtc;
            game.LaunchCount++;
            await _services.Library.SaveAsync(games).ConfigureAwait(false);
            await _services.History
                .RecordAsync(new GameSessionRecord(
                    game.Id,
                    game.Title,
                    outcome.LaunchRequestedAtUtc,
                    Duration: null,
                    Success: true,
                    Error: null))
                .ConfigureAwait(false);

            AppSettings settings = _services.SettingsSnapshot;
            if (settings.MonitoringEnabled && profile.MonitoringEnabled)
            {
                _services.Monitor.Start(
                    game,
                    outcome,
                    TimeSpan.FromMilliseconds(settings.SamplingIntervalMs));
            }
        }
        else
        {
            await _services.History
                .RecordFailureAsync(game.Id, game.Title, outcome.Message)
                .ConfigureAwait(false);
            throw new BridgeHandlerException(outcome.Message);
        }

        return new
        {
            outcome,
            session = _services.Monitor.Status,
            sample = _services.Monitor.CurrentSample
        };
    }
}

/// <summary>game.openFolder - opens the folder containing the game executable.</summary>
public sealed class GameOpenFolderHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public GameOpenFolderHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.GameOpenFolder;

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

        bool opened = _services.Launcher.OpenInstallationFolder(game);

        return new { opened };
    }
}
