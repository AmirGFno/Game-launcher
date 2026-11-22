using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Performance;

/// <summary>
/// Samples a running game process on a fixed interval.
/// <para>
/// Measurements come from <see cref="System.Diagnostics.Process"/> (CPU time,
/// working set, thread count). Nothing is estimated and no GPU or FPS value is
/// produced - those would be fabricated.
/// </para>
/// </summary>
public interface IProcessMonitorService
{
    /// <summary>True while the sampler is active.</summary>
    bool IsMonitoring { get; }

    /// <summary>The most recent real sample, or <c>null</c> before the first tick.</summary>
    PerformanceSample? CurrentSample { get; }

    /// <summary>Current session state.</summary>
    LaunchSessionInfo Status { get; }

    /// <summary>Raised on every sample.</summary>
    event EventHandler<PerformanceSample>? SampleUpdated;

    /// <summary>Raised once when the game process exits or monitoring stops.</summary>
    event EventHandler<LaunchSessionInfo>? SessionEnded;

    /// <summary>
    /// Starts monitoring the process produced by <paramref name="launch"/>.
    /// <para>
    /// For an <c>.exe</c> launch the started process is tracked directly. For a URI
    /// launch the monitor watches for <see cref="LaunchProfile.ProcessNameForMonitoring"/>;
    /// without it, monitoring is reported as <see cref="MonitoringState.Unavailable"/>
    /// rather than pretending a process exists.
    /// </para>
    /// </summary>
    void Start(GameItem game, LaunchOutcome launch, TimeSpan? samplingInterval = null);

    /// <summary>Stops monitoring and returns the final status.</summary>
    LaunchSessionInfo Stop();
}
