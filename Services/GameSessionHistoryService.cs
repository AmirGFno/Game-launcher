using System.IO;
using System.Text.Json;

namespace GameLauncher.Services;

/// <summary>
/// One recorded launch attempt. Only facts the launcher actually observed are
/// stored - no estimates, no performance numbers.
/// </summary>
/// <param name="Duration">
/// How long the game process was observed running. <c>null</c> when the launch
/// failed or the process was never seen (URI launch without a process name).
/// </param>
public sealed record GameSessionRecord(
    Guid GameId,
    string GameTitle,
    DateTime LaunchedAtUtc,
    TimeSpan? Duration,
    bool Success,
    string? Error);

/// <summary>
/// Local, lightweight history of launch attempts, used by the Performance Center.
/// <para>
/// Stored in <c>%LocalAppData%\GameLauncher\history.json</c>, capped so it stays
/// small, and written with the same atomic swap as the library.
/// </para>
/// </summary>
public interface IGameSessionHistoryService
{
    string FilePath { get; }

    IReadOnlyList<GameSessionRecord> Load();

    Task RecordAsync(GameSessionRecord record);

    /// <summary>Records a failed launch so the UI can show it.</summary>
    Task RecordFailureAsync(Guid gameId, string gameTitle, string error);

    Task ClearAsync();
}

public sealed class GameSessionHistoryService : IGameSessionHistoryService
{
    /// <summary>Upper bound on stored records, so the file cannot grow forever.</summary>
    public const int MaxRecords = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public GameSessionHistoryService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "history.json"))
    {
    }

    public GameSessionHistoryService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public string FilePath => _filePath;

    public IReadOnlyList<GameSessionRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<GameSessionRecord>();
        }

        try
        {
            using FileStream stream = File.OpenRead(_filePath);
            var records = JsonSerializer.Deserialize<List<GameSessionRecord>>(stream, JsonOptions);
            return records is null
                ? Array.Empty<GameSessionRecord>()
                : records.OrderByDescending(static r => r.LaunchedAtUtc).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<GameSessionRecord>();
        }
    }

    public async Task RecordAsync(GameSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = Load().ToList();
            records.Add(record);

            // Newest first, capped.
            records = records
                .OrderByDescending(static r => r.LaunchedAtUtc)
                .Take(MaxRecords)
                .ToList();

            await WriteAsync(records).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task RecordFailureAsync(Guid gameId, string gameTitle, string error)
    {
        await RecordAsync(new GameSessionRecord(
            gameId,
            gameTitle,
            DateTime.UtcNow,
            Duration: null,
            Success: false,
            error)).ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteAsync(new List<GameSessionRecord>()).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task WriteAsync(List<GameSessionRecord> records)
    {
        string directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        Directory.CreateDirectory(directory);

        string temporaryPath = _filePath + ".tmp";
        string backupPath = _filePath + ".bak";

        await using (FileStream stream = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        if (File.Exists(_filePath))
        {
            try
            {
                File.Replace(temporaryPath, _filePath, backupPath);
            }
            catch (IOException)
            {
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
        }
        else
        {
            File.Move(temporaryPath, _filePath);
        }
    }
}
