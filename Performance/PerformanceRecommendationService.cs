using System.Diagnostics;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Utilities;
using MemoryStatus = GameLauncher.Utilities.SystemIntegration.MemoryStatus;

namespace GameLauncher.Performance;

public enum RecommendationSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// One recommendation. <see cref="Explanation"/> always states the evidence the
/// recommendation came from, so nothing is presented as a generic "boost".
/// </summary>
public sealed record PerformanceRecommendation(
    string Code,
    RecommendationSeverity Severity,
    string Title,
    string Explanation);

/// <summary>
/// A real snapshot of the machine, measured rather than guessed.
/// </summary>
public sealed record SystemPerformanceSnapshot(
    double TotalCpuPercent,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    double AvailableMemoryPercent,
    int ProcessCount,
    DateTime CapturedAtUtc);

/// <summary>
/// Produces recommendations from measured state only.
/// <para>
/// There is deliberately no "clean RAM", "boost FPS" or registry tweak here: every
/// item must point at a concrete observation (a missing file, a measured memory
/// percentage, a measured CPU percentage, a launch that failed).
/// </para>
/// </summary>
public interface IPerformanceRecommendationService
{
    SystemPerformanceSnapshot GetSystemSnapshot();

    IReadOnlyList<PerformanceRecommendation> Analyze(
        GameItem game,
        LaunchSessionInfo? session,
        PerformanceSample? sample);

    IReadOnlyList<PerformanceRecommendation> AnalyzeSystem();
}

public sealed class PerformanceRecommendationService : IPerformanceRecommendationService
{
    /// <summary>Available physical memory below this percentage is "memory pressure".</summary>
    private const double LowMemoryPercent = 10.0;

    /// <summary>Below this, memory pressure is critical.</summary>
    private const double CriticalMemoryPercent = 5.0;

    /// <summary>Absolute floor used together with the percentage.</summary>
    private const long LowMemoryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>A game using more than this share of the whole machine is flagged.</summary>
    private const double HighGameCpuPercent = 90.0;

    /// <summary>Overall machine CPU above this is flagged.</summary>
    private const double HighSystemCpuPercent = 90.0;

    /// <summary>More running processes than this is reported as a busy machine.</summary>
    private const int BusyProcessCount = 250;

    private readonly object _cpuGate = new();
    private DateTime _lastCpuSampleUtc = DateTime.MinValue;
    private double _cachedCpuPercent;
    private ulong _lastTotalCpu;
    private ulong _lastIdleCpu;

    public SystemPerformanceSnapshot GetSystemSnapshot()
    {
        MemoryStatus memory = SystemIntegration.GetMemoryStatus();

        double availablePercent = memory.TotalBytes > 0
            ? Math.Clamp(memory.AvailablePercent, 0, 100)
            : 0;

        return new SystemPerformanceSnapshot(
            TotalCpuPercent: Math.Round(MeasureTotalCpuPercent(), 1),
            TotalMemoryBytes: memory.TotalBytes,
            AvailableMemoryBytes: memory.AvailableBytes,
            AvailableMemoryPercent: Math.Round(availablePercent, 1),
            ProcessCount: CountProcesses(),
            CapturedAtUtc: DateTime.UtcNow);
    }

    public IReadOnlyList<PerformanceRecommendation> Analyze(
        GameItem game,
        LaunchSessionInfo? session,
        PerformanceSample? sample)
    {
        ArgumentNullException.ThrowIfNull(game);

        var recommendations = new List<PerformanceRecommendation>();

        string target = game.LaunchTarget?.Trim() ?? string.Empty;

        if (target.Length == 0)
        {
            recommendations.Add(new PerformanceRecommendation(
                "target.missing",
                RecommendationSeverity.Critical,
                "No launch target",
                "This game has no launch target, so it cannot be started. Edit the game and choose an executable or a store URI."));
        }
        else if (!GameTarget.IsValid(target))
        {
            recommendations.Add(new PerformanceRecommendation(
                "target.invalid",
                RecommendationSeverity.Critical,
                "Invalid launch target",
                $"The launch target \"{target}\" is not an .exe path and not one of the supported store protocols."));
        }
        else if (!GameTarget.IsUri(target) && !File.Exists(target))
        {
            recommendations.Add(new PerformanceRecommendation(
                "target.notfound",
                RecommendationSeverity.Critical,
                "Game executable not found",
                $"The file \"{target}\" does not exist. The game may have been moved or uninstalled."));
        }

        LaunchProfile profile = game.LaunchProfile ?? new LaunchProfile();

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            && !Directory.Exists(profile.WorkingDirectory))
        {
            recommendations.Add(new PerformanceRecommendation(
                "workingdir.missing",
                RecommendationSeverity.Warning,
                "Working directory does not exist",
                $"The configured working directory \"{profile.WorkingDirectory}\" was not found. The launch will fall back to the executable's folder."));
        }

        if (!LaunchProfile.IsSupportedPriority(profile.ProcessPriority))
        {
            recommendations.Add(new PerformanceRecommendation(
                "priority.invalid",
                RecommendationSeverity.Warning,
                "Unsupported process priority",
                $"\"{profile.ProcessPriority}\" is not a supported priority. It will be treated as Normal."));
        }

        if (!GameTarget.IsUri(target) && IsAlreadyRunning(target))
        {
            recommendations.Add(new PerformanceRecommendation(
                "game.alreadyrunning",
                RecommendationSeverity.Info,
                "Game appears to already be running",
                "A process with the same image name as this game's executable is already running. Launching again may open a second instance."));
        }

        if (session is not null)
        {
            if (!session.IsUri && session.State == MonitoringState.Ended && session.GameProcessDetectedAtUtc is null)
            {
                recommendations.Add(new PerformanceRecommendation(
                    "launch.failed",
                    RecommendationSeverity.Warning,
                    "The game process closed immediately",
                    "The last launch produced no running process. The game may have crashed on startup or exited right away."));
            }

            if (session.IsUri && session.State == MonitoringState.Unavailable)
            {
                recommendations.Add(new PerformanceRecommendation(
                    "monitoring.unavailable",
                    RecommendationSeverity.Info,
                    "Progress cannot be monitored for this launch",
                    "This game launches through a store protocol and no game process name is configured, so the launcher cannot tell when the real game starts. Set the process name in the editor to enable monitoring."));
            }

            if (!string.IsNullOrWhiteSpace(session.Error))
            {
                recommendations.Add(new PerformanceRecommendation(
                    "launch.error",
                    RecommendationSeverity.Warning,
                    "The last launch reported an error",
                    session.Error));
            }
        }

        if (sample is not null && sample.CpuPercent >= HighGameCpuPercent)
        {
            recommendations.Add(new PerformanceRecommendation(
                "game.highcpu",
                RecommendationSeverity.Warning,
                "The game is using almost the whole CPU",
                $"The last sample reported {sample.CpuPercent:F1}% of total CPU capacity. That is expected for a demanding game, but it leaves little for anything else."));
        }

        recommendations.AddRange(AnalyzeSystem());

        return recommendations;
    }

    public IReadOnlyList<PerformanceRecommendation> AnalyzeSystem()
    {
        var recommendations = new List<PerformanceRecommendation>();

        SystemPerformanceSnapshot snapshot = GetSystemSnapshot();

        if (snapshot.AvailableMemoryPercent <= CriticalMemoryPercent
            && snapshot.AvailableMemoryBytes < LowMemoryBytes)
        {
            recommendations.Add(new PerformanceRecommendation(
                "memory.critical",
                RecommendationSeverity.Critical,
                "Available memory is very low",
                $"Only {FormatBytes(snapshot.AvailableMemoryBytes)} of {FormatBytes(snapshot.TotalMemoryBytes)} is available ({snapshot.AvailableMemoryPercent:F1}%). Games may stutter or fail to load textures."));
        }
        else if (snapshot.AvailableMemoryPercent <= LowMemoryPercent
                 && snapshot.AvailableMemoryBytes < LowMemoryBytes)
        {
            recommendations.Add(new PerformanceRecommendation(
                "memory.low",
                RecommendationSeverity.Warning,
                "Memory pressure detected",
                $"Only {FormatBytes(snapshot.AvailableMemoryBytes)} of {FormatBytes(snapshot.TotalMemoryBytes)} is available ({snapshot.AvailableMemoryPercent:F1}%). Closing other applications may help."));
        }

        if (snapshot.TotalCpuPercent >= HighSystemCpuPercent)
        {
            recommendations.Add(new PerformanceRecommendation(
                "system.highcpu",
                RecommendationSeverity.Warning,
                "The machine is almost fully busy",
                $"Total CPU usage is {snapshot.TotalCpuPercent:F1}%. Other applications are competing with the game for processor time."));
        }

        if (snapshot.ProcessCount > BusyProcessCount)
        {
            recommendations.Add(new PerformanceRecommendation(
                "system.manyprocesses",
                RecommendationSeverity.Info,
                "Many applications are running",
                $"{snapshot.ProcessCount} processes are running. This is not necessarily a problem, but it is the reason a busy machine can feel slower."));
        }

        return recommendations;
    }

    /// <summary>
    /// Overall CPU usage, measured by summing the CPU time of every process over a
    /// short interval and normalising by the logical processor count.
    /// <para>
    /// This uses Windows GetSystemTimes CPU-time deltas, including protected processes,
    /// and is cached for a second so repeated calls are cheap.
    /// </para>
    /// </summary>
    private double MeasureTotalCpuPercent()
    {
        lock (_cpuGate)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastCpuSampleUtc).TotalSeconds < 1.0 && _lastCpuSampleUtc != DateTime.MinValue)
            {
                return _cachedCpuPercent;
            }

            if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
                throw new System.ComponentModel.Win32Exception();
            ulong total = kernel + user; // Windows kernel time includes idle time.
            double percent = _cachedCpuPercent;
            if (_lastCpuSampleUtc != DateTime.MinValue && total > _lastTotalCpu)
            {
                ulong elapsed = total - _lastTotalCpu;
                ulong idleElapsed = idle - _lastIdleCpu;
                percent = Math.Clamp(100.0 * (1.0 - (double)idleElapsed / elapsed), 0, 100);
            }
            _lastTotalCpu = total;
            _lastIdleCpu = idle;
            _lastCpuSampleUtc = now;
            _cachedCpuPercent = Math.Round(percent, 1);
            return _cachedCpuPercent;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    private static int CountProcesses()
    {
        try
        {
            Process[] processes = Process.GetProcesses();
            int count = processes.Length;
            foreach (Process process in processes) process.Dispose();
            return count;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// True when a process with the same image name is already running. Used for the
    /// "already running" warning; it never terminates anything.
    /// </summary>
    private static bool IsAlreadyRunning(string executablePath)
    {
        try
        {
            string imageName = Path.GetFileNameWithoutExtension(executablePath);
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
                    // Ignore a process that vanished.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Human-readable byte size, used in the explanation text.</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
