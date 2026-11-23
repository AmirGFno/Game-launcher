using System.IO;

namespace GameLauncher.Services;

/// <summary>Only immediate, app-generated image files; never recursive, never custom artwork.</summary>
public sealed class ArtworkCacheService
{
    public Task<(int Deleted, int Skipped)> ClearAsync() => Task.Run(() =>
    {
        string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncher");
        int deleted=0, skipped=0;
        foreach (string name in new[] { "Images", "Icons" })
        {
            string root = PathSafety.EnsureSafeRoot(Path.Combine(appRoot,name));
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".ico")) continue;
                if (!PathSafety.IsSafeToDelete(file,root)) { skipped++; continue; }
                try { File.Delete(file); deleted++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
        }
        return (deleted,skipped);
    });
}
