using System.Diagnostics;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Services;

public sealed class GameLauncherService : IGameLauncherService
{
    private readonly GameProcessWatcher _watcher = new();
    public void Launch(GameItem game) { using var process = Start(game); }
    public async Task LaunchAndWaitAsync(GameItem game, CancellationToken token)
    {
        await Task.Run(async () => {
            var launchedAt = DateTime.UtcNow;
            using var process = Start(game);
            await _watcher.WaitAsync(game, process, launchedAt, token);
        }, token);
    }
    public Task<string> CloseTrackedAsync(CancellationToken token) => _watcher.CloseTrackedAsync(token);
    private static Process? Start(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string target = game.LaunchTarget?.Trim() ?? string.Empty;
        if (target.Length == 0)
        {
            throw new InvalidOperationException("This game has no launch target.");
        }

        if (!GameTarget.IsValid(target))
            throw new InvalidOperationException("Use an executable path or an allowed game-store URI.");

        var startInfo = new ProcessStartInfo { UseShellExecute = true };

        if (GameTarget.IsUri(target))
        {
            // steam://rungameid/12345, https://... - let the shell resolve it.
            startInfo.FileName = target;
        }
        else
        {
            if (!File.Exists(target))
            {
                throw new FileNotFoundException($"The game executable was not found:\n{target}", target);
            }

            startInfo.FileName = target;
            startInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? string.Empty;
        }

        return Process.Start(startInfo);
    }
}
