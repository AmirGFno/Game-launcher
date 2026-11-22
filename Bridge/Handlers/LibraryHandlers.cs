using System.IO;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Bridge.Handlers;

/// <summary>library.get - returns the whole library.</summary>
public sealed class LibraryGetHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public LibraryGetHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.LibraryGet;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<GameItem> games = await _services.Library.LoadAsync().ConfigureAwait(false);
        return new { games };
    }
}

/// <summary>library.create - adds a game. The id is assigned by the backend.</summary>
public sealed class LibraryCreateHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public LibraryCreateHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.LibraryCreate;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        GameItem? incoming = request.Payload.GetGame();
        if (incoming is null)
        {
            throw new BridgeHandlerException("The request did not contain a game.");
        }

        if (string.IsNullOrWhiteSpace(incoming.Title))
        {
            throw new BridgeHandlerException("The game needs a name.");
        }

        if (!GameTarget.IsValid(incoming.LaunchTarget))
        {
            throw new BridgeHandlerException(
                "The launch target is not valid. Use an .exe path or one of the supported store protocols.");
        }

        // The server owns the identity and the priority range.
        incoming.Id = Guid.NewGuid();
        incoming.LaunchProfile ??= new LaunchProfile();
        incoming.LaunchProfile.ProcessPriority =
            LaunchProfile.NormalizePriority(incoming.LaunchProfile.ProcessPriority);

        List<GameItem> games = (await _services.Library.LoadAsync().ConfigureAwait(false)).ToList();
        games.Add(incoming);

        await _services.Library.SaveAsync(games).ConfigureAwait(false);

        return new { game = incoming, games };
    }
}

/// <summary>library.update - replaces a stored game, preserving its id.</summary>
public sealed class LibraryUpdateHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public LibraryUpdateHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.LibraryUpdate;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");
        if (id is null)
        {
            throw new BridgeHandlerException("The request did not contain a game id.");
        }

        GameItem? incoming = request.Payload.GetGame();
        if (incoming is null)
        {
            throw new BridgeHandlerException("The request did not contain a game.");
        }

        List<GameItem> games = (await _services.Library.LoadAsync().ConfigureAwait(false)).ToList();
        int index = games.FindIndex(game => game.Id == id.Value);
        if (index < 0)
        {
            throw new BridgeHandlerException("That game is no longer in the library.");
        }

        if (string.IsNullOrWhiteSpace(incoming.Title))
        {
            throw new BridgeHandlerException("The game needs a name.");
        }

        if (!GameTarget.IsValid(incoming.LaunchTarget))
        {
            throw new BridgeHandlerException(
                "The launch target is not valid. Use an .exe path or one of the supported store protocols.");
        }

        // Keep the stored identity and the manual position; take everything else.
        incoming.Id = id.Value;
        incoming.LastPlayed = games[index].LastPlayed;
        incoming.LaunchCount = games[index].LaunchCount;
        incoming.LaunchProfile ??= new LaunchProfile();
        incoming.LaunchProfile.ProcessPriority =
            LaunchProfile.NormalizePriority(incoming.LaunchProfile.ProcessPriority);

        games[index] = incoming;

        await _services.Library.SaveAsync(games).ConfigureAwait(false);

        return new { game = incoming, games };
    }
}

/// <summary>library.delete - removes a game by id.</summary>
public sealed class LibraryDeleteHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public LibraryDeleteHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.LibraryDelete;

    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");
        if (id is null)
        {
            throw new BridgeHandlerException("The request did not contain a game id.");
        }

        List<GameItem> games = (await _services.Library.LoadAsync().ConfigureAwait(false)).ToList();
        int removed = games.RemoveAll(game => game.Id == id.Value);

        if (removed == 0)
        {
            throw new BridgeHandlerException("That game is no longer in the library.");
        }

        await _services.Library.SaveAsync(games).ConfigureAwait(false);

        return new { games };
    }
}

/// <summary>game.detect - reads the display name and icon out of an executable.</summary>
public sealed class GameDetectHandler : IBridgeHandler
{
    private readonly BridgeServices _services;

    public GameDetectHandler(BridgeServices services) => _services = services;

    public string Type => BridgeContract.GameDetect;

    public Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        string? path = request.Payload.GetString("path");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new BridgeHandlerException("That executable was not found.");
        }

        string title = GameInfoExtractor.GetDisplayName(path);
        string? logoPath = GameInfoExtractor.ExtractLogo(path);

        return Task.FromResult<object?>(new { title, logoPath });
    }
}

/// <summary>Moves one game a single position without replacing any metadata.</summary>
public sealed class LibraryReorderHandler(BridgeServices services) : IBridgeHandler
{
    public string Type => BridgeContract.LibraryReorder;
    public async Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Guid? id = request.Payload.GetGuid("id");
        int direction = request.Payload.GetInt32("direction", 0);
        if (id is null || (direction != -1 && direction != 1))
            throw new BridgeHandlerException("Select a game and a valid reorder direction.");
        var games = (await services.Library.LoadAsync().ConfigureAwait(false)).ToList();
        int index = games.FindIndex(game => game.Id == id);
        if (index < 0) throw new BridgeHandlerException("That game is no longer available.");
        int target = index + direction;
        if (target >= 0 && target < games.Count)
        {
            (games[index], games[target]) = (games[target], games[index]);
            await services.Library.SaveAsync(games).ConfigureAwait(false);
        }
        return new { games };
    }
}
