using System.Security;
using Microsoft.Win32;

namespace GameLauncher.WebView;

/// <summary>Result of checking for the WebView2 Runtime.</summary>
public sealed record WebView2RuntimeInfo(bool IsInstalled, string? Version)
{
    /// <summary>
    /// Microsoft's official WebView2 distribution page. The runtime is never
    /// bundled or redistributed by this project.
    /// </summary>
    public const string OfficialDownloadUrl =
        "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static WebView2RuntimeInfo Missing()
        => new(false, null);

    public static WebView2RuntimeInfo Found(string? version)
        => new(true, version);
}

/// <summary>
/// Checks whether the Microsoft Edge WebView2 Runtime is installed, by reading the
/// same registry keys the WebView2 installer writes.
/// <para>
/// The runtime is a Windows component. This launcher never bundles it and never
/// downloads it silently - if it is missing the app explains what is needed and
/// points at Microsoft's official distribution page.
/// </para>
/// </summary>
public static class WebView2RuntimeChecker
{
    public static WebView2RuntimeInfo Check()
    {
        try
        {
            return WebView2RuntimeInfo.Found(
                Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            return WebView2RuntimeInfo.Missing();
        }
    }
}
