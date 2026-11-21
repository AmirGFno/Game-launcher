using System.Diagnostics;
using GameLauncher.Models;

namespace GameLauncher.Services;

public sealed class GameLauncherService : IGameLauncherService
{
    public void Launch(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string target = game.LaunchTarget.Trim();
        if (target.Length == 0)
        {
            throw new InvalidOperationException("This game has no launch target.");
        }

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
            startInfo.WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty;
        }

        Process.Start(startInfo);
    }
}
