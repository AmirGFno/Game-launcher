using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Starts the process for a game (either an .exe or a steam:// style URI).
/// </summary>
public interface IGameLauncherService
{
    void Launch(GameItem game);
    Task LaunchAndWaitAsync(GameItem game, CancellationToken token)
        => Task.Run(() => Launch(game), token);
    Task<string> CloseTrackedAsync(CancellationToken token) => Task.FromResult("No tracked processes.");
}
