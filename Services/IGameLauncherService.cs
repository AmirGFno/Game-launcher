using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// The outcome of a launch attempt.
/// <para>
/// The timestamps are kept separate on purpose: for a <c>steam://</c> style launch
/// Windows only confirms that the store client received the request, so
/// <see cref="LaunchRequestedAtUtc"/> and <see cref="GameProcessDetectedAtUtc"/>
/// are genuinely different moments and must not be reported as one.
/// </para>
/// </summary>
public sealed record LaunchOutcome
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid GameId { get; init; }

    public string LaunchTarget { get; init; } = string.Empty;

    /// <summary>True when the target is an allowlisted URI rather than an .exe.</summary>
    public bool IsUri { get; init; }

    public int? ProcessId { get; init; }

    public string? ProcessName { get; init; }

    /// <summary>When the launcher handed the target to Windows.</summary>
    public DateTime LaunchRequestedAtUtc { get; init; }

    /// <summary>
    /// When an actual game process existed. <c>null</c> for URI launches where no
    /// process handle is available.
    /// </summary>
    public DateTime? GameProcessDetectedAtUtc { get; init; }

    public static LaunchOutcome Failed(GameItem game, string message, DateTime requestedAtUtc)
        => new()
        {
            Success = false,
            Message = message,
            GameId = game.Id,
            LaunchTarget = game.LaunchTarget,
            IsUri = GameTarget.IsUri(game.LaunchTarget),
            LaunchRequestedAtUtc = requestedAtUtc
        };
}

/// <summary>
/// Starts the process for a game (either an .exe or a steam:// style URI) and
/// applies the per-game launch profile.
/// </summary>
public interface IGameLauncherService
{
    /// <summary>
    /// Launches <paramref name="game"/>. Never throws for an expected failure
    /// (missing file, invalid target, shell error): the reason is returned in the
    /// outcome so the UI can show it.
    /// </summary>
    LaunchOutcome Launch(GameItem game);

    /// <summary>
    /// Opens the folder that contains the game executable in Windows Explorer.
    /// Returns <c>false</c> when there is no folder to open.
    /// </summary>
    bool OpenInstallationFolder(GameItem game);
}
