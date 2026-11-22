using System.IO;
using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Stores the library in <c>%LocalAppData%\GameLauncher\library.json</c>.
/// <para>
/// Saves are serialised and atomic: the JSON is written to a temporary file, that
/// file is flushed and closed, and only then is it swapped over the live file with
/// <see cref="File.Replace"/>, which also keeps a <c>.bak</c> copy. A corrupted file
/// is moved aside (never deleted) so the library is always recoverable.
/// </para>
/// </summary>
public sealed class GameLibraryService : IGameLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public GameLibraryService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "library.json"))
    {
    }

    /// <param name="filePath">
    /// Allows tests (and future alternate profiles) to use an isolated file.
    /// </param>
    public GameLibraryService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>The live library file.</summary>
    public string FilePath => _filePath;

    public async Task<IReadOnlyList<GameItem>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<GameItem>();
        }

        try
        {
            await using FileStream stream = File.OpenRead(_filePath);
            var games = await JsonSerializer.DeserializeAsync<List<GameItem>>(stream, JsonOptions)
                                             .ConfigureAwait(false);
            return games ?? new List<GameItem>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Never lose the whole library because of a corrupted file: move it
            // aside under a unique name and start with an empty library.
            PreserveCorruptFile();

            return new List<GameItem>();
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<GameItem> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        // One writer at a time: overlapping fire-and-forget saves must not interleave
        // their temp files or replace the live file with a partial document.
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
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
                await JsonSerializer
                    .SerializeAsync(stream, games, JsonOptions)
                    .ConfigureAwait(false);

                await stream.FlushAsync().ConfigureAwait(false);
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    // Atomic on the same volume and keeps the previous file as .bak.
                    File.Replace(temporaryPath, _filePath, backupPath);
                }
                catch (IOException)
                {
                    // Cross-volume / locked destination - fall back to an overwrite.
                    File.Move(temporaryPath, _filePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            string corrupt = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            File.Move(_filePath, corrupt);
        }
        catch
        {
            // Best effort only - the user can still restore the file by hand.
        }
    }
}
