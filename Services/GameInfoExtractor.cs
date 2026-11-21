using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GameLauncher.Services;

/// <summary>
/// Reads the display name and the embedded icon straight out of a game executable,
/// so the user does not have to type them or hunt for artwork.
/// </summary>
public static class GameInfoExtractor
{
    private static readonly string IconsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLauncher",
        "Icons");

    /// <summary>
    /// The executable's <c>FileDescription</c>, falling back to the file name
    /// without its extension when the executable carries no description.
    /// </summary>
    public static string GetDisplayName(string exePath)
    {
        try
        {
            FileVersionInfo? versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            if (versionInfo is not null && !string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                return versionInfo.FileDescription.Trim();
            }
        }
        catch
        {
            // Not a readable PE file - fall through to the file name.
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }

    /// <summary>
    /// Extracts the icon embedded in <paramref name="exePath"/>, converts it to a
    /// WPF <see cref="BitmapSource"/> and stores it as a PNG next to the library.
    /// Returns the path of the stored file, or <c>null</c> when no icon was found.
    /// </summary>
    public static string? ExtractLogo(string exePath)
    {
        try
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                return null;
            }

            using Bitmap? bitmap = icon.ToBitmap();
            if (bitmap is null)
            {
                return null;
            }

            BitmapSource source = ConvertToBitmapSource(bitmap);

            Directory.CreateDirectory(IconsDirectory);
            string targetPath = Path.Combine(IconsDirectory, $"{Guid.NewGuid():N}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using FileStream stream = File.Create(targetPath);
            encoder.Save(stream);

            return targetPath;
        }
        catch
        {
            // A missing / unreadable icon must never break the add-game dialog.
            return null;
        }
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
