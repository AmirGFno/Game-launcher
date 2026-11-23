using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using GameLauncher.Models;

namespace GameLauncher.Services;

public sealed record SteamGame(uint AppId, string Name);
public sealed class SteamScannerService
{
    public static string LogoUrl(uint id) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg";
    public static string BackgroundUrl(uint id) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_hero.jpg";
    public static IReadOnlyList<SteamGame> ReadManifests(string steamApps)
    {
        var result = new List<SteamGame>();
        if (!Directory.Exists(steamApps)) return result;
        foreach (string file in Directory.EnumerateFiles(steamApps,"appmanifest_*.acf",SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (new FileInfo(file).Length > 1024*1024) continue;
                string text = File.ReadAllText(file);
                string id = Value(text,"appid"), name = Value(text,"name");
                if (uint.TryParse(id,out uint appId) && appId > 0 && name.Length > 0) result.Add(new(appId,name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
    private static string Value(string text,string key) => Regex.Match(text, "\"" + key + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Groups[1].Value;
    public IReadOnlyList<SteamGame> Scan()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string root = key?.GetValue("SteamPath") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        string libraries = Path.Combine(root,"steamapps","libraryfolders.vdf");
        if (File.Exists(libraries) && new FileInfo(libraries).Length < 4*1024*1024)
            foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        return roots.SelectMany(path => ReadManifests(Path.Combine(path,"steamapps"))).DistinctBy(g => g.AppId).ToList();
    }
    public async Task<GameItem> CreateAsync(SteamGame game, CancellationToken token)
    {
        var item = new GameItem { Title = game.Name, LaunchTarget = $"steam://rungameid/{game.AppId}" };
        try { item.LogoPath = await ImageDownloader.DownloadAsync(LogoUrl(game.AppId),token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* Offline/missing artwork never prevents importing a game. */ }
        try { item.BackgroundPath = await ImageDownloader.DownloadAsync(BackgroundUrl(game.AppId),token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        return item;
    }
}
