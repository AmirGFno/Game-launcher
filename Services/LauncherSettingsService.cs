using System.IO;
using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Services;

public sealed class LauncherSettingsService
{
    private readonly string _path;
    public LauncherSettingsService(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "settings.json");
    public LauncherSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            settings.CarouselStyle = settings.CarouselStyle is "FixedAnchor" or "Fixed Anchor" ? "FixedAnchor" : "Loop";
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
        else File.Move(temp, _path);
    }
}
