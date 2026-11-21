using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Loads and stores the game library as JSON inside the local AppData folder.
/// </summary>
public interface IGameLibraryService
{
    Task<IReadOnlyList<GameItem>> LoadAsync();

    Task SaveAsync(IReadOnlyCollection<GameItem> games);
}
