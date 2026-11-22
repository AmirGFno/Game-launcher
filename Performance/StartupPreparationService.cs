using System.Diagnostics;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Performance;

/// <summary>One pre-flight check and whether it passed.</summary>
public sealed record PreparationCheck(string Code, bool Passed, string Message);

/// <summary>
/// The outcome of the optional pre-flight check. <see cref="CanLaunch"/> is false
/// only when a critical check failed.
/// </summary>
public sealed record PreparationResult(IReadOnlyList<PreparationCheck> Checks, bool CanLaunch)
{
    public IReadOnlyList<PreparationCheck> Failed => Checks.Where(static c => !c.Passed).ToList();
}

/// <summary>Result of asking one application to close.</summary>
public sealed record GracefulCloseOutcome(string Application, bool Closed, string Message);

/// <summary>
/// Optional, opt-in launch preparation.
/// <para>
/// Hard rules: it never calls <c>Kill()</c>, never touches a process the user did not
/// name, and never disables security software. Asking an application to close is done
/// with <see cref="Process.CloseMainWindow"/> and a bounded wait; if the application
/// does not respond it is reported as still running and left alone.
/// </para>
/// </summary>
public interface IStartupPreparationService
{
    /// <summary>Runs the pre-flight checks without changing anything.</summary>
    PreparationResult Prepare(GameItem game);

    /// <summary>True when a process with the game's image name is already running.</summary>
    bool IsAlreadyRunning(GameItem game);

    /// <summary>
    /// Asks the named applications to close gracefully. Cancellable; never forceful.
    /// </summary>
    Task<IReadOnlyList<GracefulCloseOutcome>> CloseApplicationsAsync(
        IReadOnlyList<string> applicationNames,
        IProgress<string> progress,
        CancellationToken cancellationToken = default);
}

public sealed class StartupPreparationService : IStartupPreparationService
{
    /// <summary>How long to wait for a graceful close before reporting it as stuck.</summary>
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(8);

    public PreparationResult Prepare(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var checks = new List<PreparationCheck>();
        string target = game.LaunchTarget?.Trim() ?? string.Empty;

        checks.Add(new PreparationCheck(
            "target.present",
            target.Length > 0,
            target.Length > 0
                ? "A launch target is configured."
                : "No launch target is configured."));

        bool targetValid = GameTarget.IsValid(target);
        checks.Add(new PreparationCheck(
            "target.valid",
            targetValid,
            targetValid
                ? "The launch target is valid."
                : "The launch target is neither an .exe path nor a supported store protocol."));

        if (targetValid && !GameTarget.IsUri(target))
        {
            bool exists = File.Exists(target);
            checks.Add(new PreparationCheck(
                "executable.exists",
                exists,
                exists
                    ? "The game executable exists."
                    : $"The game executable was not found: {target}"));

            checks.Add(new PreparationCheck(
                "game.notrunning",
                !IsAlreadyRunning(game),
                IsAlreadyRunning(game)
                    ? "The game appears to already be running."
                    : "The game is not currently running."));
        }

        LaunchProfile profile = game.LaunchProfile ?? new LaunchProfile();

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            bool directoryExists = Directory.Exists(profile.WorkingDirectory);
            checks.Add(new PreparationCheck(
                "workingdir.exists",
                directoryExists,
                directoryExists
                    ? "The configured working directory exists."
                    : $"The working directory does not exist: {profile.WorkingDirectory}"));
        }

        if (!string.IsNullOrWhiteSpace(profile.CommandLineArguments)
            && profile.CommandLineArguments.Contains('\0', StringComparison.Ordinal))
        {
            checks.Add(new PreparationCheck(
                "arguments.valid",
                false,
                "The launch arguments contain a NUL character and cannot be passed to Windows."));
        }

        if (profile.SafeStartupPreparationEnabled)
        {
            int configured = profile.PreLaunchApplications?.Count ?? 0;
            checks.Add(new PreparationCheck(
                "preparation.configured",
                true,
                configured == 0
                    ? "Safe startup preparation is enabled, but no applications are configured to close."
                    : $"Safe startup preparation will ask {configured} configured application(s) to close."));
        }

        bool canLaunch = checks.All(static check => check.Passed);

        return new PreparationResult(checks, canLaunch);
    }

    public bool IsAlreadyRunning(GameItem game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string target = game.LaunchTarget?.Trim() ?? string.Empty;
        if (GameTarget.IsUri(target) || !File.Exists(target))
        {
            return false;
        }

        string imageName = Path.GetFileNameWithoutExtension(target);
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return false;
        }

        foreach (Process process in Process.GetProcessesByName(imageName))
        {
            try
            {
                if (!process.HasExited)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process vanished - treat as not running.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<GracefulCloseOutcome>> CloseApplicationsAsync(
        IReadOnlyList<string> applicationNames,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationNames);
        ArgumentNullException.ThrowIfNull(progress);

        var results = new List<GracefulCloseOutcome>();

        foreach (string rawName in applicationNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = rawName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            // Accept both "app" and "app.exe".
            string imageName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            progress.Report($"Asking {imageName} to close...");

            List<Process> targets;
            try
            {
                targets = Process.GetProcessesByName(imageName).ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                results.Add(new GracefulCloseOutcome(imageName, false, "The process list could not be read."));
                continue;
            }

            if (targets.Count == 0)
            {
                results.Add(new GracefulCloseOutcome(imageName, true, "It was not running."));
                continue;
            }

            bool anyStuck = false;
            foreach (Process process in targets)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    // Graceful request only. No Kill(), ever.
                    if (!process.CloseMainWindow())
                    {
                        anyStuck = true;
                        continue;
                    }

                    if (!process.WaitForExit((int)CloseTimeout.TotalMilliseconds))
                    {
                        anyStuck = true;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                          or System.ComponentModel.Win32Exception
                                          or UnauthorizedAccessException)
                {
                    anyStuck = true;
                }
                finally
                {
                    process.Dispose();
                }
            }

            results.Add(new GracefulCloseOutcome(
                imageName,
                !anyStuck,
                anyStuck
                    ? "It did not close within the timeout. It was left running - nothing was forced."
                    : $"Closed {targets.Count} instance(s) gracefully."));
        }

        return results;
    }
}
