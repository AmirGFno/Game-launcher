using GameLauncher.Models;

namespace GFnoLauncher.Tests;

public sealed class GameTargetTests
{
    [Theory]
    [InlineData("steam://rungameid/12345", true)]
    [InlineData("com.epicgames.launcher://apps/ABC?action=launch", true)]
    [InlineData("uplay://launch/123/0", true)]
    [InlineData("goggalaxy://runGame/abc", true)]
    [InlineData(@"C:\Games\Demo\demo.exe", true)]
    [InlineData("demo.exe", true)]
    [InlineData("http://example.com/game", false)]
    [InlineData("https://example.com/game", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("custom-scheme://run", false)]
    [InlineData("notepad", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValid(string target, bool expected)
    {
        Assert.Equal(expected, GameTarget.IsValid(target));
    }

    [Theory]
    [InlineData("steam://rungameid/1", true)]
    [InlineData("http://example.com", false)]
    [InlineData(@"C:\Games\demo.exe", false)]
    public void IsUri_onlyAcceptsTheAllowlist(string target, bool expected)
    {
        Assert.Equal(expected, GameTarget.IsUri(target));
    }

    [Fact]
    public void AllowedUriSchemes_containsSteam()
    {
        Assert.Contains("steam", GameTarget.AllowedUriSchemes, StringComparer.OrdinalIgnoreCase);
    }
}
