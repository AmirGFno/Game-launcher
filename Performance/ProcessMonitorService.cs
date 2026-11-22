using System.Diagnostics;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Performance;

/// <summary>
/// Samples one game process every <c>SamplingInterval</c> (default 1.5 s).
/// <para>
/// CPU usage is computed from process CPU-time deltas over elapsed wall-clock time,
/// normalised by <see cref="Environment.ProcessorCount"/>, which is the standard
/// way to express "percentage of the whole machine". A single sample is noisy, so
/// the UI shows the latest value and the history keeps the trend.
/// </para>
/// <para>
/// The sampler timer is only running while a game is being monitored, so an idle
/// launcher costs nothing.
/// </para>
/// </summary>
public sealed class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    /// <summary>Default sample interval.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>Lower bound, so a setting cannot turn the sampler into a busy loop.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;

    private Process? _process;

    /// <summary>Process image name watched for a URI-launched game.</summary>
    private string? _watchName;

    private Guid _gameId;
    private string _gameTitle = string.Empty;
    private string _launchTarget = string.Empty;
    private bool _isUri;
    private DateTime _launchRequestedAtUtc;
    private DateTime? _gameProcessDetectedAtUtc;
    private DateTime? _sessionStartedAtUtc;
    private DateTime? _sessionEndedAtUtc;
    private MonitoringState _state = MonitoringState.NotStarted;
    private string? _error;
    private TimeSpan _interval = DefaultInterval;
    private TimeSpan _lastTotalProcessorTime;
    private DateTime _lastSampleUtc;
    private bool _disposed;

    public ProcessMonitorService()
    {
        // Created once, but only runs while a game is monitored.
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsMonitoring
    {
        get { lock (_gate) { return _state is MonitoringState.Running or MonitoringState.WaitingForProcess; } }
    }

    public PerformanceSample? CurrentSample
    {
        get { lock (_gate) { return _currentSample; } }
    }

    private PerformanceSample? _currentSample;

    public LaunchSessionInfo Status
    {
        get { lock (_gate) { return BuildStatus(); } }
    }

    public event EventHandler<PerformanceSample>? SampleUpdated;

    public event EventHandler<LaunchSessionInfo>? SessionEnded;

    public void Start(GameItem game, LaunchOutcome launch, TimeSpan? samplingInterval = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(launch);

        TimeSpan interval = NormalizeInterval(samplingInterval);

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProcessMonitorService));
            }

            if (_state is MonitoringState.Running or MonitoringState.WaitingForProcess)
                FinishSession();
            StopTimerLocked();

            _gameId = game.Id;
            _gameTitle = game.Title;
            _launchTarget = launch.LaunchTarget;
            _isUri = launch.IsUri;
            _launchRequestedAtUtc = launch.LaunchRequestedAtUtc;
            _gameProcessDetectedAtUtc = launch.GameProcessDetectedAtUtc;
            _sessionStartedAtUtc = null;
            _sessionEndedAtUtc = null;
            _error = launch.Success ? null : launch.Message;
            _currentSample = null;
            _process = null;
            _watchName = null;
            _lastTotalProcessorTime = TimeSpan.Zero;
            _lastSampleUtc = DateTime.UtcNow;
            _interval = interval;

            if (!launch.Success)
            {
                _state = MonitoringState.Ended;
                _error = launch.Message;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            if (!launch.IsUri && launch.ProcessId is { } processId)
            {
                try
                {
                    // Re-attach to the process we started. It may already have
                    // exited (a launcher stub), which is reported honestly.
                    _process = Process.GetProcessById(processId);
                    if (_process.HasExited)
                    {
                        _process.Dispose();
                        _process = null;
                        _state = MonitoringState.Ended;
                        _timer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    _process = null;
                    _state = MonitoringState.Ended;
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }
                catch (InvalidOperationException)
                {
                    _process = null;
                    _state = MonitoringState.Ended;
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                BeginSession(_process);
                _timer.Change(TimeSpan.Zero, interval);
                return;
            }

            // URI launch: a process handle only exists if the user named one.
            _watchName = game.LaunchProfile?.ProcessNameForMonitoring;
            if (string.IsNullOrWhiteSpace(_watchName))
            {
                _state = MonitoringState.Unavailable;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            _state = MonitoringState.WaitingForProcess;
            _timer.Change(TimeSpan.Zero, interval);
        }
    }

    public LaunchSessionInfo Stop()
    {
        lock (_gate)
        {
            StopTimerLocked();

            if (_state is MonitoringState.Running or MonitoringState.WaitingForProcess)
            {
                _sessionEndedAtUtc = DateTime.UtcNow;
                _state = MonitoringState.Ended;
            }

            LaunchSessionInfo status = BuildStatus();
            SessionEnded?.Invoke(this, status);
            return status;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _process?.Dispose();
            _process = null;
        }

        _timer.Dispose();
    }

    /// <summary>
    /// Starts sampling an already-running process (used when a URI-launched game is
    /// discovered by name).
    /// </summary>
    private void BeginSession(Process process)
    {
        _process = process;
        _sessionStartedAtUtc = DateTime.UtcNow;
        _gameProcessDetectedAtUtc = _gameProcessDetectedAtUtc ?? DateTime.UtcNow;
        _lastTotalProcessorTime = SafeTotalProcessorTime(process);
        _lastSampleUtc = DateTime.UtcNow;
        _state = MonitoringState.Running;
    }

    private void OnTick(object? state)
    {
        PerformanceSample? sample = null;

        lock (_gate)
        {
            if (_disposed || _state is not (MonitoringState.Running or MonitoringState.WaitingForProcess))
            {
                return;
            }

            try
            {
                if (_state == MonitoringState.WaitingForProcess)
                {
                    if (!TryAttachProcessByName())
                    {
                        return;
                    }
                }

                if (_process is null || _process.HasExited)
                {
                    FinishSession();
                    return;
                }

                _process.Refresh();

                DateTime now = DateTime.UtcNow;
                TimeSpan total = SafeTotalProcessorTime(_process);
                double elapsedSeconds = (now - _lastSampleUtc).TotalSeconds;

                double cpuPercent = 0;
                if (elapsedSeconds > 0.25)
                {
                    double cpuSeconds = (total - _lastTotalProcessorTime).TotalSeconds;
                    cpuPercent = cpuSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100.0;
                    cpuPercent = Math.Clamp(cpuPercent, 0.0, 100.0);
                }

                _lastTotalProcessorTime = total;
                _lastSampleUtc = now;

                _currentSample = new PerformanceSample(
                    TimestampUtc: now,
                    ProcessId: _process.Id,
                    ProcessName: SafeProcessName(_process),
                    CpuPercent: Math.Round(cpuPercent, 1),
                    WorkingSetBytes: SafeWorkingSet(_process),
                    ThreadCount: SafeThreadCount(_process),
                    TotalProcessorTime: total,
                    SessionDuration: now - _sessionStartedAtUtc.GetValueOrDefault(now),
                    ProcessStartTimeUtc: SafeStartTime(_process));

                sample = _currentSample;
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and the property reads.
                FinishSession();
            }
            catch (Exception)
            {
                // A transient read failure must not kill the sampler; the next tick
                // retries.
            }
        }

        if (sample is not null)
        {
            SampleUpdated?.Invoke(this, sample);
        }
    }

    /// <summary>
    /// Looks for the configured game process. A process that was already running
    /// before the launch request is ignored, so an unrelated instance is never
    /// reported as "the game".
    /// </summary>
    private bool TryAttachProcessByName()
    {
        string? watchName = _watchName;
        if (string.IsNullOrWhiteSpace(watchName))
        {
            return false;
        }

        try
        {
            Process[] candidates = Process.GetProcessesByName(watchName);
            foreach (Process candidate in candidates)
            {
                try
                {
                    DateTime started = candidate.StartTime.ToUniversalTime();
                    if (started < _launchRequestedAtUtc.AddSeconds(-5))
                    {
                        continue;
                    }

                    BeginSession(candidate);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                          or System.ComponentModel.Win32Exception)
                {
                    // Process vanished while we were reading it.
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        return false;
    }

    private void FinishSession()
    {
        _sessionEndedAtUtc = DateTime.UtcNow;
        _state = MonitoringState.Ended;
        StopTimerLocked();

        LaunchSessionInfo status = BuildStatus();
        SessionEnded?.Invoke(this, status);
    }

    private LaunchSessionInfo BuildStatus()
    {
        DateTime? sessionStart = _sessionStartedAtUtc;
        DateTime? sessionEnd = _sessionEndedAtUtc;

        TimeSpan? duration = sessionStart is { } start
            ? (sessionEnd ?? DateTime.UtcNow) - start
            : null;

        return new LaunchSessionInfo(
            GameId: _gameId,
            GameTitle: _gameTitle,
            LaunchTarget: _launchTarget,
            IsUri: _isUri,
            State: _state,
            LaunchRequestedAtUtc: _launchRequestedAtUtc,
            GameProcessDetectedAtUtc: _gameProcessDetectedAtUtc,
            SessionStartedAtUtc: sessionStart,
            SessionEndedAtUtc: sessionEnd,
            ProcessId: _process?.Id,
            ProcessName: _process is null ? null : SafeProcessName(_process),
            SessionDuration: duration,
            Error: _error);
    }

    private void StopTimerLocked()
    {
        try
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Dispose already ran.
        }
    }

    private static TimeSpan NormalizeInterval(TimeSpan? interval)
    {
        if (interval is null || interval.Value <= TimeSpan.Zero)
        {
            return DefaultInterval;
        }

        return interval.Value < MinimumInterval ? MinimumInterval : interval.Value;
    }

    private static TimeSpan SafeTotalProcessorTime(Process process)
    {
        try
        {
            return process.TotalProcessorTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return TimeSpan.Zero;
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private static int SafeThreadCount(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static DateTime? SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
