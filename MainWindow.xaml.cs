using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.Bridge;
using GameLauncher.Bridge.Handlers;
using GameLauncher.Models;
using GameLauncher.NativeServices;
using GameLauncher.Performance;
using GameLauncher.Services;
using GameLauncher.Utilities;
using GameLauncher.WebView;
using GameLauncher.WindowManagement;
using Microsoft.Web.WebView2.Core;

namespace GameLauncher;

/// <summary>
/// Thin native host. It owns the window, the WebView2 control, the system tray icon
/// and the bridge; everything the user sees is the React frontend served from a
/// local virtual host.
/// </summary>
public partial class MainWindow : Window
{
    private const string AppHostName = "app.gfno.local";
    private const string CacheHostName = "cache.gfno.local";
    private const string AppHostUrl = "https://app.gfno.local/index.html";
    private const string AppOrigin = "https://" + AppHostName;

    private BridgeServices? _services;
    private BridgeServer? _bridge;
    private WindowController? _windowController;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings = new();
    private string _wwwRoot = string.Empty;
    private bool _interfaceReady;
    private bool _disposed;
    private bool _closing;
    private readonly object _historyGate = new();
    private Task _historyWrites = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnWindowSizeStateChanged;
    }

    // ---- Lifecycle -------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        _settings = new SettingsService().Load();
        _windowController = new WindowController(this);

        SetupTrayIcon();
        SetupBridge();

        try
        {
            await InitializeWebViewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowFatal(
                "The interface could not be started.\n\n" + ex.Message);
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        e.Cancel = true;
        if (_closing) return;
        _closing = true;

        try
        {
            _services?.Monitor.Stop();
            (_services?.Monitor as IDisposable)?.Dispose();
        }
        catch
        {
            // Best effort during shutdown.
        }

        Task pending;
        lock (_historyGate) pending = _historyWrites;
        await pending;
        _trayIcon?.Dispose();
        _trayIcon = null;
        WebView.Dispose();
        _disposed = true;
        Close();
    }

    // ---- WebView2 --------------------------------------------------------

    private async Task InitializeWebViewAsync()
    {
        WebView2RuntimeInfo runtime = WebView2RuntimeChecker.Check();
        if (!runtime.IsInstalled)
        {
            ShowRuntimeMissing(runtime);
            return;
        }

        _wwwRoot = LocateWebRoot();
        if (!Directory.Exists(_wwwRoot))
        {
            ShowFatal(
                "The interface files were not found next to the application.\n\n"
                + "Expected folder: wwwroot\n"
                + "Reinstall the application or download the package again.");
            return;
        }

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLauncher",
            "WebView2");

        Directory.CreateDirectory(userDataFolder);

        OverlayMessage.Text = "Starting the interface...";
        OverlayProgress.Visibility = Visibility.Visible;

        CoreWebView2Environment environment = await CoreWebView2Environment
            .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder)
            .ConfigureAwait(true);

        await WebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        CoreWebView2 core = WebView.CoreWebView2;

        // Production hardening: no dev tools, no context menu, no zoom control, no
        // accelerator keys, no popups, and no navigation away from the local host.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsScriptEnabled = true;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDevToolsEnabled = false;

        // Serve the packaged frontend and the thumbnail cache from virtual hosts.
        // Nothing is served from localhost and nothing depends on a dev server.
        core.SetVirtualHostNameToFolderMapping(
            AppHostName,
            _wwwRoot,
            CoreWebView2HostResourceAccessKind.Allow);

        core.SetVirtualHostNameToFolderMapping(
            CacheHostName,
            _services?.Thumbnails.CacheDirectory ?? string.Empty,
            CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;

        WebView.Focus();
        core.Navigate(AppHostUrl);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Uri))
        {
            return;
        }

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && string.Equals(uri.Host, AppHostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Never render external content inside the launcher. External links are
        // handed to the default browser instead.
        e.Cancel = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? external)
            && (external.Scheme == Uri.UriSchemeHttp || external.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = external.AbsoluteUri
                });
            }
            catch
            {
                // No default browser - nothing else to do.
            }
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_interfaceReady && e.IsSuccess)
        {
            _interfaceReady = true;
            Overlay.Visibility = Visibility.Collapsed;
            WebView.Focus();
            return;
        }

        if (!e.IsSuccess && !_interfaceReady)
        {
            ShowFatal(
                "The interface could not be loaded (HTTP error "
                + e.WebErrorStatus + ").\n\n"
                + "Check that the wwwroot folder sits next to GFnoLauncher.exe.");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.WebMessageAsJson;

        string origin = string.Empty;
        try
        {
            string? source = e.Source;
            if (!string.IsNullOrEmpty(source) && Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
            {
                origin = uri.GetLeftPart(UriPartial.Authority);
            }
        }
        catch
        {
            origin = string.Empty;
        }

        _bridge?.HandleRawMessage(json, origin);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // The launcher never opens child windows.
        e.Handled = true;
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _interfaceReady = false;
        ShowFatal(
            "The interface process failed (" + e.ProcessFailedKind + ").\n\n"
            + "Restart the application. If it keeps happening, reinstall the WebView2 Runtime.");
    }

    /// <summary>
    /// Locates the packaged frontend. In a installed / portable run this is the
    /// wwwroot folder next to the executable; when running from a build output the
    /// repository's frontend/dist is used so the app stays runnable in development.
    /// </summary>
    private static string LocateWebRoot()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string packaged = Path.Combine(baseDirectory, "wwwroot");

        if (Directory.Exists(packaged))
        {
            return packaged;
        }

        if (!baseDirectory.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
        {
            return packaged;
        }

        // Development fallback: <repo>/frontend/dist
        string? directory = new DirectoryInfo(baseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        if (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "frontend", "dist");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return packaged;
    }

    /// <summary>Posts a message to the frontend on the UI thread.</summary>
    private void PostMessage(string json)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                WebView?.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch
            {
                // The web view is going away.
            }
        });
    }

    // ---- Bridge ------------------------------------------------------

    private void SetupBridge()
    {
        var library = new GameLibraryService();
        var launcher = new GameLauncherService();
        var cleaner = new TempCleanerService();
        var thumbnails = new ThumbnailService();
        var settings = new SettingsService();
        var history = new GameSessionHistoryService();
        var monitor = new ProcessMonitorService();
        var recommendations = new PerformanceRecommendationService();
        var preparation = new StartupPreparationService();

        _services = new BridgeServices(
            library,
            launcher,
            cleaner,
            thumbnails,
            settings,
            history,
            monitor,
            recommendations,
            preparation,
            new NativeDialogService(this),
            _windowController!,
            "https://" + CacheHostName,
            GetAppVersion());

        _services.SettingsSnapshot = _settings;
        _services.EventSink = (type, payload) => _bridge?.PostEvent(type, payload);
        _services.SettingsApplied = ApplySettings;

        IBridgeHandler[] handlers =
        {
            new LibraryGetHandler(_services),
            new LibraryCreateHandler(_services),
            new LibraryUpdateHandler(_services),
            new LibraryDeleteHandler(_services),
            new LibraryReorderHandler(_services),
            new GameDetectHandler(_services),
            new GameLaunchHandler(_services),
            new GameOpenFolderHandler(_services),
            new ImageDownloadHandler(_services),
            new ImageThumbnailHandler(_services),
            new ImageDominantColorHandler(_services),
            new SettingsGetHandler(_services),
            new SettingsSaveHandler(_services),
            new PerformanceStatusHandler(_services),
            new PerformanceStartHandler(_services),
            new PerformanceStopHandler(_services),
            new PerformanceRecommendationsHandler(_services),
            new CachePreviewHandler(_services),
            new CacheCleanHandler(_services),
            new WindowMinimizeHandler(_services),
            new WindowMaximizeHandler(_services),
            new WindowCloseHandler(_services),
            new WindowSetFullscreenHandler(_services),
            new DialogOpenFileHandler(_services),
            new DialogOpenFolderHandler(_services),
            new SystemOpenPathHandler(_services),
            new AppVersionHandler(_services),
            new AppRuntimeStatusHandler(_services)
        };

        _bridge = new BridgeServer(handlers, PostMessage, AppOrigin);

        monitor.SampleUpdated += (_, sample) => _bridge?.PostEvent("performance.sample", sample);
        monitor.SessionEnded += (_, status) =>
        {
            _bridge?.PostEvent("performance.sessionEnded", status);
            if (status.GameId == Guid.Empty || status.State != MonitoringState.Ended) return;
            lock (_historyGate)
            {
                _historyWrites = _historyWrites.ContinueWith(async _ =>
                {
                    try
                    {
                        await history.RecordAsync(new GameSessionRecord(status.GameId,
                            status.GameTitle, status.LaunchRequestedAtUtc, status.SessionDuration,
                            status.Error is null, status.Error)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Session history could not be saved: " + ex.Message);
                        _bridge?.PostEvent("history.error", new { message = "Session history could not be saved." });
                    }
                }, TaskScheduler.Default).Unwrap();
            }
        };
        _windowController!.StateChanged += (_, state) =>
            _bridge?.PostEvent("window.state.changed", new { state = state.ToString() });
    }

    /// <summary>
    /// Applies settings that the desktop host owns: tray behaviour and the
    /// per-user "start with Windows" entry.
    /// </summary>
    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        StartupLaunch.SetEnabled(settings.StartWithWindows, GetExecutablePath());
    }

    private static string GetExecutablePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName
                   ?? System.Windows.Forms.Application.ExecutablePath;
        }
        catch
        {
            return System.Windows.Forms.Application.ExecutablePath;
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "2.1.1" : version.ToString(3);
        }
        catch
        {
            return "2.1.1";
        }
    }

    // ---- System tray -----------------------------------------------------

    private void SetupTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");

        System.Drawing.Icon? icon = null;
        try
        {
            if (File.Exists(iconPath))
            {
                icon = new System.Drawing.Icon(iconPath);
            }
        }
        catch
        {
            icon = null;
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "GFno Launcher",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open GFno Launcher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => Close());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    internal void ShowFromTray()
    {
        if (_closing || _disposed) return;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowSizeStateChanged(object? sender, EventArgs e)
    {
        if (_settings.MinimizeToTray && WindowState == WindowState.Minimized)
        {
            // Hide() removes the taskbar button; the tray icon keeps the app reachable.
            Hide();
        }
    }

    // ---- Fallback UI -----------------------------------------------------

    private void ShowRuntimeMissing(WebView2RuntimeInfo runtime)
    {
        Overlay.Visibility = Visibility.Visible;
        OverlayProgress.Visibility = Visibility.Collapsed;

        OverlayTitle.Text = "WebView2 Runtime is required";
        OverlayMessage.Text =
            "GFno Launcher needs the Microsoft Edge WebView2 Runtime, which is not "
            + "installed on this PC.\n\n"
            + "It is a free Windows component. Install it from Microsoft's official "
            + "page, then start GFno Launcher again.";

        OverlayAction.Content = "Open the official WebView2 download page";
        OverlayAction.Visibility = Visibility.Visible;
        OverlayAction.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = WebView2RuntimeInfo.OfficialDownloadUrl
                });
            }
            catch
            {
                // Nothing else to do.
            }
        };
    }

    /// <summary>
    /// Shows the native fallback panel with an explanation. There is deliberately no
    /// "retry": initialisation only runs once, and a second attempt would re-subscribe
    /// the WebView2 events.
    /// </summary>
    private void ShowFatal(string message)
    {
        Overlay.Visibility = Visibility.Visible;
        OverlayProgress.Visibility = Visibility.Collapsed;

        OverlayTitle.Text = "GFno Launcher";
        OverlayMessage.Text = message;
        OverlayAction.Visibility = Visibility.Collapsed;
    }
}
