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
    public static uint? AppIdFromTarget(string? target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != "steam") return null;
        if (uri.Host is not ("run" or "rungameid")) return null;
        return uint.TryParse(uri.AbsolutePath.Trim('/').Split('/')[0], out uint id) && id > 0 ? id : null;
    }
    public static IEnumerable<string> ArtworkUrls(uint id, bool hero)
    {
        string[] files = hero ? new[] { "library_hero.jpg", "page_bg_generated_v6b.jpg", "header.jpg" }
            : new[] { "library_600x900.jpg", "library_capsule.jpg", "capsule_616x353.jpg", "header.jpg", "logo.png" };
        string[] roots = { "https://cdn.cloudflare.steamstatic.com/steam/apps/", "https://cdn.akamai.steamstatic.com/steam/apps/", "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" };
        foreach (string root in roots) foreach (string file in files) yield return $"{root}{id}/{file}";
    }
    public async Task<GameItem> CreateAsync(SteamGame game, CancellationToken token, IProgress<string>? progress = null)
    {
        var item = new GameItem { Title = game.Name, LaunchTarget = $"steam://rungameid/{game.AppId}" };
        item.LogoPath = await GetArtworkAsync(game.AppId, false, token, progress);
        item.BackgroundPath = await GetArtworkAsync(game.AppId, true, token, progress);
        return item;
    }
    internal static async Task<string?> GetArtworkAsync(uint id, bool hero, CancellationToken token, IProgress<string>? progress = null,
        Func<string,string,CancellationToken,Task<string>>? download = null, string? directory = null)
    {
        string stem = $"steam_{id}_{(hero ? "hero" : "logo")}";
        directory ??= ImageDownloader.ImagesDirectory;
        foreach (string extension in new[] { ".jpg", ".png", ".webp", ".gif", ".bmp", ".ico" })
        {
            string cached = Path.Combine(directory,stem+extension);
            if (!File.Exists(cached)) continue;
            try { await Task.Run(() => ImageDownloader.ValidateImageFile(cached),token); return cached; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* Invalid cached files are retried, never treated as success. */ }
        }
        download ??= ImageDownloader.DownloadStableAsync;
        int attempt = 0;
        foreach (string url in ArtworkUrls(id,hero))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Steam {id}: downloading {(hero ? "background" : "logo")} (attempt {++attempt})…");
            try { return await download(url,stem,token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Steam artwork failed: {url}: {ex.Message}"); }
        }
        progress?.Report($"Steam {id}: artwork unavailable; game imported without {(hero ? "background" : "logo")}.");
        return null;
    }
}
