using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>Readiness observation is not ownership. Only Process.Start identities are eligible for Clean.</summary>
public sealed class GameProcessWatcher
{
    private readonly Dictionary<int, (DateTime Start, string Path)> _owned = new();
    internal static async Task<bool> PollAsync(Func<bool> ready, int attempts, Func<CancellationToken, Task> delay, CancellationToken token)
    {
        for (int i = 0; i < attempts; i++)
        {
            token.ThrowIfCancellationRequested();
            if (ready()) return true;
            await delay(token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return false;
    }

    public async Task WaitAsync(GameItem game, Process? started, DateTime launchedAt, CancellationToken token)
    {
        bool direct = !GameTarget.IsUri(game.LaunchTarget);
        string? executable = direct ? Path.GetFullPath(game.LaunchTarget) : null;
        if (direct && started is not null)
        {
            try
            {
                string? actual = started.MainModule?.FileName;
                if (string.Equals(actual, executable, StringComparison.OrdinalIgnoreCase))
                    lock (_owned) _owned[started.Id] = (started.StartTime.ToUniversalTime(), actual!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        uint? appId = SteamScannerService.AppIdFromTarget(game.LaunchTarget);
        string? install = game.SteamInstallDirectory;
        if (!direct && install is null && appId.HasValue)
        {
            try { install = new SteamScannerService().Scan().FirstOrDefault(g => g.AppId == appId)?.InstallDirectory; }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        int observations = 0;
        bool Ready()
        {
            observations++;
            if (direct && started is not null)
            {
                try
                {
                    started.Refresh();
                    // Give short-lived bootstrap executables time to exit before accepting liveness.
                    if (!started.HasExited && (started.MainWindowHandle != IntPtr.Zero || observations >= 4)) return true;
                }
                catch (InvalidOperationException) { }
            }
            if (appId.HasValue)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}");
                    if (key?.GetValue("Running") is int running && running == 1) return true;
                }
                catch (System.Security.SecurityException) { }
            }
            // Path + creation time, never name alone. Observed processes are NOT added to Clean's owned list.
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                try
                {
                    if (process.Id == Environment.ProcessId || process.StartTime.ToUniversalTime() < launchedAt.AddSeconds(-1)) continue;
                    string? path = process.MainModule?.FileName;
                    if (path is null) continue;
                    if (direct && string.Equals(path, executable, StringComparison.OrdinalIgnoreCase)) return true;
                    if (install is not null && path.StartsWith(Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            return false;
        }
        // Steam shell activation does not prove a game started. After eight seconds of observation,
        // yield the screen anyway rather than covering Steam prompts for up to 90 seconds.
        bool detected = await PollAsync(Ready, direct ? 12 : 8, ct => Task.Delay(1000, ct), token).ConfigureAwait(false);
        if (!detected && direct) throw new InvalidOperationException("The executable exited before a running game could be confirmed. The launcher has been restored.");
    }

    public Task<string> CloseTrackedAsync(CancellationToken token) => Task.Run(async () =>
    {
        KeyValuePair<int, (DateTime Start, string Path)>[] identities;
        lock (_owned) identities = _owned.ToArray();
        int closed = 0, skipped = 0;
        foreach (var entry in identities)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(entry.Key);
                // Reused PIDs and shell-resolved Steam clients never confer ownership.
                if (process.StartTime.ToUniversalTime() != entry.Value.Start ||
                    !string.Equals(process.MainModule?.FileName, entry.Value.Path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.ProcessName, "steam", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (!process.CloseMainWindow()) { skipped++; continue; }
                if (await PollAsync(() => process.HasExited, 10, ct => Task.Delay(100, ct), token)) closed++;
                else skipped++; // No forced Kill, no elevation, no killing an unverified descendant tree.
            }
            catch (ArgumentException) { }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { skipped++; Debug.WriteLine(ex); }
        }
        return $"Tracked processes closed: {closed}; skipped or still running: {skipped}.";
    }, token);
}
