using GameLauncher.Models;
using GameLauncher.Services;

namespace GFnoLauncher.Tests;

public sealed class GameLibraryServiceTests : IDisposable
{
    private readonly string _directory;

    public GameLibraryServiceTests()
    {
        _directory = Path.Combine(AppContext.BaseDirectory, "gfno-lib-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private string FilePath => Path.Combine(_directory, "library.json");

    [Fact]
    public async Task SaveThenLoad_preservesEveryField()
    {
        var service = new GameLibraryService(FilePath);
        var game = new GameItem
        {
            Title = "Half-Life 3",
            LaunchTarget = @"C:\Games\hl3\hl3.exe",
            LogoPath = @"C:\Games\hl3\logo.png",
            BackgroundPath = @"C:\Games\hl3\bg.png",
            CustomCacheFolderPath = @"C:\Games\hl3\cache",
            LastPlayed = new DateTime(2026, 1, 2, 3, 4, 5),
            LaunchCount = 7,
            IsFavorite = true,
            Category = "FPS"
        };
        game.SetDominantColor(System.Windows.Media.Colors.OrangeRed);

        await service.SaveAsync(new[] { game });

        IReadOnlyList<GameItem> loaded = await service.LoadAsync();
        GameItem restored = Assert.Single(loaded);

        Assert.Equal(game.Id, restored.Id);
        Assert.Equal(game.Title, restored.Title);
        Assert.Equal(game.LaunchTarget, restored.LaunchTarget);
        Assert.Equal(game.LogoPath, restored.LogoPath);
        Assert.Equal(game.BackgroundPath, restored.BackgroundPath);
        Assert.Equal(game.CustomCacheFolderPath, restored.CustomCacheFolderPath);
        Assert.Equal(game.LastPlayed, restored.LastPlayed);
        Assert.Equal(7, restored.LaunchCount);
        Assert.True(restored.IsFavorite);
        Assert.Equal("FPS", restored.Category);
        Assert.Equal(game.DominantColor, restored.DominantColor);
        Assert.True(restored.RestoreDominantColor());
        Assert.Equal(game.DominantColorValue, restored.DominantColorValue);
    }

    [Fact]
    public async Task SaveAsync_keepsABackupOfThePreviousFile()
    {
        var service = new GameLibraryService(FilePath);

        await service.SaveAsync(new[] { new GameItem { Title = "First" } });
        await service.SaveAsync(new[] { new GameItem { Title = "Second" } });

        string backup = FilePath + ".bak";
        Assert.True(File.Exists(backup), "the previous library should be kept as .bak");

        string backupJson = await File.ReadAllTextAsync(backup);
        Assert.Contains("First", backupJson);
        Assert.DoesNotContain("Second", backupJson);

        string liveJson = await File.ReadAllTextAsync(FilePath);
        Assert.Contains("Second", liveJson);
    }

    [Fact]
    public async Task LoadAsync_movesACorruptFileAsideInsteadOfDeletingIt()
    {
        string path = FilePath;
        await File.WriteAllTextAsync(path, "{ this is not json");

        var service = new GameLibraryService(path);
        IReadOnlyList<GameItem> loaded = await service.LoadAsync();

        Assert.Empty(loaded);
        Assert.False(File.Exists(path), "the corrupt file should be moved, not deleted");
        Assert.True(Directory.GetFiles(_directory, "*.corrupt-*").Length == 1);
    }

    [Fact]
    public async Task SaveAsync_isSerialised_soConcurrentWritesCannotCorruptTheFile()
    {
        var service = new GameLibraryService(FilePath);

        var first = Enumerable.Range(0, 20)
            .Select(i => new GameItem { Title = "A" + i })
            .ToList();
        var second = Enumerable.Range(0, 20)
            .Select(i => new GameItem { Title = "B" + i })
            .ToList();

        await Task.WhenAll(
            service.SaveAsync(first),
            service.SaveAsync(second),
            service.SaveAsync(first));

        // Whatever won, the file must be valid JSON with exactly 20 entries.
        IReadOnlyList<GameItem> loaded = await service.LoadAsync();
        Assert.Equal(20, loaded.Count);
    }

    [Fact]
    public async Task SaveAsync_isAtomic_soNoTemporaryFileIsLeftBehind()
    {
        var service = new GameLibraryService(FilePath);
        await service.SaveAsync(new[] { new GameItem { Title = "X" } });

        Assert.False(File.Exists(FilePath + ".tmp"));
    }
}
