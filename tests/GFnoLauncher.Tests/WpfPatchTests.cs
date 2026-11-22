using System.Text.Json;
using GameLauncher.Models;
using GameLauncher.Services;
using Xunit;

namespace GFnoLauncher.Tests;

public class WpfPatchTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void LauncherRejectsUnsupportedTargets(string target)
    {
        Assert.Throws<InvalidOperationException>(() => new GameLauncherService().Launch(new GameItem { LaunchTarget = target }));
    }

    [Fact]
    public void NewerMetadataSurvivesWpfEditRoundTrip()
    {
        var game = JsonSerializer.Deserialize<GameItem>("{\"Title\":\"Example\",\"LaunchProfile\":{\"WorkingDirectory\":\"C:\\\\Games\"}}")!;
        var edited = game.Clone();
        edited.Title = "Renamed";
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(edited));
        Assert.Equal("C:\\Games", json.RootElement.GetProperty("LaunchProfile").GetProperty("WorkingDirectory").GetString());
        Assert.NotSame(game.AdditionalData, edited.AdditionalData);
    }
}
