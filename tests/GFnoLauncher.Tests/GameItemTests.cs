using System.Windows.Media;
using GameLauncher.Models;

namespace GFnoLauncher.Tests;

public sealed class GameItemTests
{
    [Fact]
    public void Clone_preservesAllMetadata_andIsIndependent()
    {
        var original = new GameItem
        {
            Title = "Original",
            LaunchTarget = @"C:\Games\demo.exe",
            LastPlayed = new DateTime(2026, 5, 6, 7, 8, 9),
            LaunchCount = 12,
            IsFavorite = true,
            Category = "RPG",
            CustomCacheFolderPath = @"C:\Games\cache"
        };
        original.SetDominantColor(Colors.DodgerBlue);

        GameItem clone = original.Clone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.LastPlayed, clone.LastPlayed);
        Assert.Equal(12, clone.LaunchCount);
        Assert.True(clone.IsFavorite);
        Assert.Equal("RPG", clone.Category);
        Assert.Equal(original.DominantColor, clone.DominantColor);
        Assert.Equal(original.DominantColorSource, clone.DominantColorSource);
        Assert.True(clone.RestoreDominantColor());

        // Editing the clone must not touch the original.
        clone.Title = "Changed";
        clone.LaunchCount = 99;
        Assert.Equal("Original", original.Title);
        Assert.Equal(12, original.LaunchCount);
    }

    [Fact]
    public void IsInstalled_reflectsWhetherTheExecutableExists()
    {
        var missing = new GameItem { LaunchTarget = @"C:\Definitely\Missing\game.exe" };
        Assert.False(missing.IsInstalled);

        var uri = new GameItem { LaunchTarget = "steam://rungameid/1" };
        Assert.True(uri.IsInstalled);
    }
}
