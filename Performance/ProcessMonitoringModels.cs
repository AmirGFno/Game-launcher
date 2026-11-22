namespace GameLauncher.Performance;

/// <summary>
/// How far the monitor got. The states are deliberately explicit so the UI can
/// tell the user the truth instead of implying a measurement that was never taken.
/// </summary>
public enum MonitoringState
{
    /// <summary>No monitoring has been started.</summary>
    NotStarted,

    /// <summary>
    /// A URI launch was submitted and the configured game process name is being
    /// watched for. Nothing has been measured yet.
    /// </summary>
    WaitingForProcess,

    /// <summary>A game process is being sampled.</summary>
    Running,

    /// <summary>The game process exited (or was never seen again).</summary>
    Ended,

    /// <summary>
    /// Monitoring is not possible: a URI launch with no process name configured,
    /// so Windows never handed back a game process.
    /// </summary>
    Unavailable
}

/// <summary>
/// One real measurement of a running game process.
/// </summary>
/// <param name="CpuPercent">
/// CPU usage as a percentage of the whole machine (all logical processors), so
/// 100% means every core is fully busy. Derived from process CPU-time deltas over
/// the elapsed wall-clock time between two samples.
/// </param>
/// <param name="WorkingSetBytes">Process working set, from <c>Process.WorkingSet64</c>.</param>
/// <param name="SessionDuration">Time since the game process was first observed.</param>
public sealed record PerformanceSample(
    DateTime TimestampUtc,
    int ProcessId,
    string ProcessName,
    double CpuPercent,
    long WorkingSetBytes,
    int ThreadCount,
    TimeSpan TotalProcessorTime,
    TimeSpan SessionDuration,
    DateTime? ProcessStartTimeUtc);

/// <summary>
/// Everything known about one launch, including the honest distinction between
/// "the request was submitted" and "a game process actually exists".
/// </summary>
public sealed record LaunchSessionInfo(
    Guid GameId,
    string GameTitle,
    string LaunchTarget,
    bool IsUri,
    MonitoringState State,
    DateTime LaunchRequestedAtUtc,
    DateTime? GameProcessDetectedAtUtc,
    DateTime? SessionStartedAtUtc,
    DateTime? SessionEndedAtUtc,
    int? ProcessId,
    string? ProcessName,
    TimeSpan? SessionDuration,
    string? Error)
{
    /// <summary>
    /// Time from the launch request to the first observed game process. This is
    /// <c>null</c> for URI launches that never produced a process - it is a real
    /// measurement, not a substitute for the launch time.
    /// </summary>
    public TimeSpan? TimeToGameProcess
        => GameProcessDetectedAtUtc is { } detected
            ? detected - LaunchRequestedAtUtc
            : null;
}
