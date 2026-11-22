using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GameLauncher.Utilities;

/// <summary>
/// Small, Windows-specific helpers used by the launcher services.
/// <para>
/// Everything here goes through the shell with <c>UseShellExecute</c>; nothing is
/// executed directly and no path is deleted.
/// </para>
/// </summary>
public static class SystemIntegration
{
    /// <summary>
    /// Opens <paramref name="path"/> in Windows Explorer. A directory opens as a
    /// folder; a file opens with its containing folder selected.
    /// </summary>
    public static bool OpenFolderInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string argument = File.Exists(path)
                ? $"/select,\"{path}\""
                : $"\"{path}\"";

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "explorer.exe",
                Arguments = argument
            };

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  or System.ComponentModel.Win32Exception
                                  or IOException)
        {
            return false;
        }
    }

    /// <summary>Opens a file or a folder with its default handler.</summary>
    public static bool OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = path
            };

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  or System.ComponentModel.Win32Exception
                                  or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Physical memory status from <c>GlobalMemoryStatusEx</c>. Used by the
    /// Performance Center so "insufficient memory" recommendations are based on a
    /// real measurement rather than a guess.
    /// </summary>
    public static MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatus
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (NativeMethods.GlobalMemoryStatusEx(ref status) == 0)
        {
            return new MemoryStatus(0, 0, 0);
        }

        long total = (long)status.TotalPhysicalBytes;
        long available = (long)status.AvailablePhysicalBytes;

        double percent = total > 0 ? (available * 100.0 / total) : 0;

        return new MemoryStatus(total, available, percent);
    }

    /// <summary>
    /// Physical memory in bytes, plus the percentage that is currently available.
    /// </summary>
    public sealed record MemoryStatus(long TotalBytes, long AvailableBytes, double AvailablePercent)
    {
        public static MemoryStatus Empty => new(0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalBytes;
        public ulong AvailablePhysicalBytes;
        public ulong TotalPageFileBytes;
        public ulong AvailablePageFileBytes;
        public ulong TotalVirtualBytes;
        public ulong AvailableVirtualBytes;
        public ulong AvailableExtendedVirtualBytes;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
