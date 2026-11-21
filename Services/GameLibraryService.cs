using System.IO;
using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Stores the library in <c>%LocalAppData%\GameLauncher\library.json</c>.
/// </summary>
public sealed class GameLibraryService : IGameLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public GameLibraryService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "library.json");
    }

    public async Task<IReadOnlyList<GameItem>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<GameItem>();
        }

        try
        {
            await using FileStream stream = File.OpenRead(_filePath);
            var games = await JsonSerializer.DeserializeAsync<List<GameItem>>(stream, JsonOptions);
            return games ?? new List<GameItem>();
        }
        catch (JsonException)
        {
            // Never lose the whole library because of a corrupted file:
            // move it aside and start with an empty library.
            try
            {
                string backup = _filePath + ".bak";
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(_filePath, backup);
            }
            catch
            {
                // Best effort only.
            }

            return new List<GameItem>();
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<GameItem> games)
    {
        string directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        Directory.CreateDirectory(directory);

        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, games, JsonOptions);
    }
}
