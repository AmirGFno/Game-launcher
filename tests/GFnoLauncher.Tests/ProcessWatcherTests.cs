using GameLauncher.Services;
using Xunit;

namespace GFnoLauncher.Tests;
public sealed class ProcessWatcherTests
{
    [Fact]
    public async Task DetectionStopsPollingImmediately()
    {
        int probes = 0, delays = 0;
        bool result = await GameProcessWatcher.PollAsync(() => ++probes == 3, 8,
            _ => { delays++; return Task.CompletedTask; }, default);
        Assert.True(result); Assert.Equal(3, probes); Assert.Equal(2, delays);
    }
    [Fact]
    public async Task GraceIsBoundedAndDoesNotClaimDetection()
    {
        int delays = 0;
        Assert.False(await GameProcessWatcher.PollAsync(() => false, 8,
            _ => { delays++; return Task.CompletedTask; }, default));
        Assert.Equal(8, delays);
    }
    [Fact]
    public async Task CancellationDoesNotMinimizeAsSuccess()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameProcessWatcher.PollAsync(() => true,
            8, _ => Task.CompletedTask, cts.Token));
    }
    [Fact]
    public async Task CleanWithNoOwnedProcessesDoesNotTouchOtherApps()
    {
        Assert.Contains("closed: 0", await new GameProcessWatcher().CloseTrackedAsync(default));
    }
}
