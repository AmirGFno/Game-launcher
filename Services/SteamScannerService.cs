using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using GameLauncher.Models;

namespace GameLauncher.Services;

public sealed record SteamGame(uint AppId, string Name, string? InstallDirectory = null);
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
                if (uint.TryParse(id,out uint appId) && appId > 0 && name.Length > 0) {
                    string folder = Value(text,"installdir");
                    string common = Path.GetFullPath(Path.Combine(steamApps,"common"));
                    string? install = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(Path.Combine(common,folder));
                    if (install is not null && !install.StartsWith(common + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) install = null;
                    result.Add(new(appId,name,install));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return result;
    }
    private static string Value(string text,string key) => Regex.Match(text, "\"" + key + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Groups[1].Value;
    public IReadOnlyList<SteamGame> Scan(string? manualRoot = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string root = manualRoot ?? key?.GetValue("SteamPath") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");
        root = Path.GetFullPath(root);
        if (string.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), "steamapps", StringComparison.OrdinalIgnoreCase))
            root = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        string libraries = Path.Combine(root,"steamapps","libraryfolders.vdf");
        if (File.Exists(libraries) && new FileInfo(libraries).Length < 4*1024*1024)
            foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        return roots.SelectMany(path => {
            try { return ReadManifests(Path.Combine(path,"steamapps")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<SteamGame>(); }
        }).DistinctBy(g => g.AppId).ToList();
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
        var item = new GameItem { Title = game.Name, LaunchTarget = $"steam://rungameid/{game.AppId}", SteamInstallDirectory = game.InstallDirectory };
        var logo = GetArtworkAsync(game.AppId, false, token, progress);
        var hero = GetArtworkAsync(game.AppId, true, token, progress);
        await Task.WhenAll(logo, hero);
        item.LogoPath = await logo;
        item.BackgroundPath = await hero;
        return item;
    }
    internal static async Task<string?> GetArtworkAsync(uint id, bool hero, CancellationToken token, IProgress<string>? progress = null,
        Func<string,string,CancellationToken,Task<string>>? download = null, string? directory = null)
    {
        string stem = $"steam_{id}_{(hero ? "hero" : "logo")}";
        directory ??= ImageDownloader.ImagesDirectory;
        if (Directory.Exists(directory) && Directory.GetFiles(directory, stem + "_repaired.*").Any()) stem += "_repaired";
        foreach (string extension in new[] { ".jpg", ".png", ".webp", ".gif", ".bmp", ".ico" })
        {
            string cached = Path.Combine(directory,stem+extension);
            if (!File.Exists(cached)) continue;
            try { await Task.Run(() => ImageDownloader.ValidateImageFile(cached),token); return cached; }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // A legacy/manual corrupt file must not block every candidate, nor be overwritten.
                if (!File.Exists(cached + ".downloaded")) stem += "_repaired";
            }
        }
        download ??= ImageDownloader.DownloadStableAsync;
        // Bound the whole fallback matrix, not just each HTTP request (formerly many minutes per game).
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(40));
        int attempt = 0;
        foreach (string url in ArtworkUrls(id,hero))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Steam {id}: downloading {(hero ? "background" : "logo")} (attempt {++attempt})…");
            if (budget.IsCancellationRequested) break;
            try { return await download(url,stem,budget.Token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Steam artwork failed: {url}: {ex.Message}"); }
        }
        progress?.Report($"Steam {id}: artwork unavailable; game imported without {(hero ? "background" : "logo")}.");
        return null;
    }
}
