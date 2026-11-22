using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GameLauncher.Utilities;

/// <summary>
/// The per-user "start with Windows" entry.
/// <para>
/// Written to <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, which is the
/// user's own startup list: it needs no Administrator rights and does not affect any
/// other account. Nothing is written until the user opts in.
/// </para>
/// </summary>
public static class StartupLaunch
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GFnoLauncher";

    /// <summary>True when the per-user startup entry exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string existing && existing.Length > 0;
        }
        catch (Exception ex) when (ex is SecurityException
                                  or UnauthorizedAccessException
                                  or IOException
                                  or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the startup entry. <paramref name="executablePath"/> must be
    /// the full path of the launcher executable.
    /// </summary>
    public static void SetEnabled(bool enabled, string? executablePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (!enabled)
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            key.SetValue(ValueName, "\"" + executablePath + "\"", RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is SecurityException
                                  or UnauthorizedAccessException
                                  or IOException
                                  or ArgumentException)
        {
            // The user's startup list is a convenience, never a hard requirement.
        }
    }
}
