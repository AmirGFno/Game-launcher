using System.IO;
using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Services;

/// <summary>
/// Stores settings in <c>%LocalAppData%\GameLauncher\settings.json</c>.
/// <para>
/// Uses the same atomic, serialised write as <see cref="GameLibraryService"/>:
/// a temporary file is flushed and swapped over the live file, with a <c>.bak</c>
/// copy kept, so a crash mid-write can never leave a half-written settings file.
/// </para>
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "settings.json"))
    {
    }

    /// <param name="filePath">Test hook for an isolated settings file.</param>
    public SettingsService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings().Normalized();
        }

        try
        {
            using FileStream stream = File.OpenRead(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
            return settings is null ? new AppSettings().Normalized() : settings.Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the previous file for recovery and fall back to defaults.
            try
            {
                string corrupt = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
                File.Move(_filePath, corrupt);
            }
            catch
            {
                // Best effort - defaults are still usable.
            }

            return new AppSettings().Normalized();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings normalized = settings.Normalized();

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
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions)
                                   .ConfigureAwait(false);

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
        finally
        {
            _saveLock.Release();
        }
    }
}
