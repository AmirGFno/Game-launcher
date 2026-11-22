using GameLauncher.Utilities;
using System.Diagnostics;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Starts a game process and applies the per-game launch profile.
/// <para>
/// Safety rules preserved from v2.0.0 and extended:
/// <list type="bullet">
/// <item>Only <see cref="GameTarget"/>-validated targets are started: an allowlisted
/// URI scheme or an <c>.exe</c> path. Anything else is refused.</item>
/// <item>An executable must exist before it is started.</item>
/// <item>Only priorities in <see cref="LaunchProfile.SupportedPriorities"/> are
/// applied - <c>Realtime</c> can never be requested.</item>
/// <item>Affinity is never touched.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GameLauncherService : IGameLauncherService
{
    public LaunchOutcome Launch(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        DateTime requestedAt = DateTime.UtcNow;
        string target = game.LaunchTarget?.Trim() ?? string.Empty;

        if (target.Length == 0)
        {
            return LaunchOutcome.Failed(game, "This game has no launch target.", requestedAt);
        }

        if (!GameTarget.IsValid(target))
        {
            return LaunchOutcome.Failed(
                game,
                "The launch target is not valid. Use an .exe path or one of the supported store protocols.",
                requestedAt);
        }

        LaunchProfile profile = game.LaunchProfile ?? new LaunchProfile();

        if (GameTarget.IsUri(target))
        {
            return LaunchUri(game, target, profile, requestedAt);
        }

        return LaunchExecutable(game, target, profile, requestedAt);
    }

    public bool OpenInstallationFolder(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string target = game.LaunchTarget?.Trim() ?? string.Empty;
        if (GameTarget.IsUri(target) || !File.Exists(target))
        {
            return false;
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(target));
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        return SystemIntegration.OpenFolderInExplorer(folder);
    }

    private static LaunchOutcome LaunchExecutable(
        GameItem game,
        string target,
        LaunchProfile profile,
        DateTime requestedAt)
    {
        if (!File.Exists(target))
        {
            return LaunchOutcome.Failed(
                game,
                $"The game executable was not found:{Environment.NewLine}{target}",
                requestedAt);
        }

        string? workingDirectory = ResolveWorkingDirectory(target, profile.WorkingDirectory);
        if (workingDirectory is null)
        {
            return LaunchOutcome.Failed(
                game,
                $"The working directory does not exist:{Environment.NewLine}{profile.WorkingDirectory}",
                requestedAt);
        }

        string? arguments = profile.CommandLineArguments?.Trim();
        if (arguments is not null && arguments.Contains('\0', StringComparison.Ordinal))
        {
            return LaunchOutcome.Failed(game, "The launch arguments contain a NUL character.", requestedAt);
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = target,
            WorkingDirectory = workingDirectory
        };

        if (!string.IsNullOrEmpty(arguments))
        {
            startInfo.Arguments = arguments;
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return LaunchOutcome.Failed(game, "Windows did not start the game.", requestedAt);
            }

            ApplyPriority(process, profile.ProcessPriority);

            // The process can exit before we read its name (a stub launcher, for
            // example); that must not turn a successful launch into a failure.
            string? processName = TryGetProcessName(process);
            int processId = process.Id;

            return new LaunchOutcome
            {
                Success = true,
                Message = "The game was started.",
                GameId = game.Id,
                LaunchTarget = target,
                IsUri = false,
                ProcessId = processId,
                ProcessName = processName,
                LaunchRequestedAtUtc = requestedAt,
                GameProcessDetectedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  or System.ComponentModel.Win32Exception
                                  or IOException
                                  or UnauthorizedAccessException)
        {
            return LaunchOutcome.Failed(
                game,
                $"Windows could not start the game:{Environment.NewLine}{ex.Message}",
                requestedAt);
        }
    }

    private static LaunchOutcome LaunchUri(
        GameItem game,
        string target,
        LaunchProfile profile,
        DateTime requestedAt)
    {
        try
        {
            // Let the shell resolve the protocol handler. No process handle comes
            // back, so the outcome honestly reports "request submitted" only.
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = target
            };

            Process.Start(startInfo);

            return new LaunchOutcome
            {
                Success = true,
                Message = "The launch request was handed to Windows. Store-protocol launches do not expose a game process.",
                GameId = game.Id,
                LaunchTarget = target,
                IsUri = true,
                ProcessId = null,
                ProcessName = string.IsNullOrWhiteSpace(profile.ProcessNameForMonitoring)
                    ? null
                    : profile.ProcessNameForMonitoring,
                LaunchRequestedAtUtc = requestedAt,
                GameProcessDetectedAtUtc = null
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  or System.ComponentModel.Win32Exception
                                  or IOException
                                  or UnauthorizedAccessException)
        {
            return LaunchOutcome.Failed(
                game,
                $"Windows could not open the launch protocol:{Environment.NewLine}{ex.Message}",
                requestedAt);
        }
    }

    /// <summary>
    /// The profile's working directory when it is set and exists; otherwise the
    /// executable's own folder (the v2.0.0 behaviour).
    /// </summary>
    private static string? ResolveWorkingDirectory(string executablePath, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? string.Empty;
        }

        string full;
        try
        {
            full = Path.GetFullPath(configured);
        }
        catch (Exception ex) when (ex is ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            return null;
        }

        return Directory.Exists(full) ? full : null;
    }

    /// <summary>
    /// Applies the configured priority. Only values from
    /// <see cref="LaunchProfile.SupportedPriorities"/> are mapped; anything else
    /// keeps the default. <c>Realtime</c> is unreachable by construction.
    /// </summary>
    private static void ApplyPriority(Process process, string? priority)
    {
        ProcessPriorityClass mapped = priority switch
        {
            LaunchProfile.AboveNormal => ProcessPriorityClass.AboveNormal,
            LaunchProfile.High => ProcessPriorityClass.High,
            LaunchProfile.BelowNormal => ProcessPriorityClass.BelowNormal,
            LaunchProfile.Idle => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.Normal
        };

        if (mapped == ProcessPriorityClass.Normal)
        {
            return;
        }

        try
        {
            process.PriorityClass = mapped;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  or System.ComponentModel.Win32Exception
                                  or PlatformNotSupportedException)
        {
            // A game that already elevated or exited keeps the default priority.
        }
    }

    private static string? TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
