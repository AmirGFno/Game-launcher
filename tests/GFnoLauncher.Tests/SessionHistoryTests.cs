using System.IO;
using GameLauncher.Services;
using Xunit;

namespace GFnoLauncher.Tests;

public sealed class SessionHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gfno-history-" + Guid.NewGuid());
    [Fact]
    public async Task FinalizationUpdatesExistingLaunchAndSurvivesLateLaunchWrite()
    {
        var service = new GameSessionHistoryService(Path.Combine(_directory, "history.json"));
        var launch = new GameSessionRecord(Guid.NewGuid(), "Test", DateTime.UtcNow, null, true, null);
        await service.RecordAsync(launch);
        await service.RecordAsync(launch with { Duration = TimeSpan.FromMinutes(3) });
        await service.RecordAsync(launch);
        var saved = Assert.Single(service.Load());
        Assert.Equal(TimeSpan.FromMinutes(3), saved.Duration);
    }
    [Fact]
    public async Task ConcurrentFinalizationDoesNotDuplicateSessions()
    {
        var service = new GameSessionHistoryService(Path.Combine(_directory, "history.json"));
        var launch = new GameSessionRecord(Guid.NewGuid(), "Test", DateTime.UtcNow, null, true, null);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.RecordAsync(launch with { Duration = TimeSpan.FromSeconds(10) })));
        Assert.Single(service.Load());
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
