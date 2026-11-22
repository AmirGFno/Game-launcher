using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using GameLauncher.Services;
using GameLauncher.ViewModels;
using GameLauncher.Views;

namespace GameLauncher;

public partial class App : System.Windows.Application
{
    // A named mutex is the reliable way to detect a second launcher: it survives
    // across processes and is released automatically when the owner exits.
    private static readonly string InstanceKey = @"Local\GFnoLauncher." +
        System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
    private static readonly string SingleInstanceMutexName = InstanceKey + ".Mutex";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceKey + ".Activate");
        bool createdNew = false;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out createdNew);
        }
        catch (Exception)
        {
            // The mutex is an optimisation, not a hard requirement - if the name is
            // unavailable, keep running rather than failing to start.
        }

        if (!createdNew && _singleInstanceMutex is not null)
        {
            // Another instance is already running - bring its window forward instead
            // of starting a second copy of the launcher.
            _activationEvent.Set();
            Shutdown();
            return;
        }

        // Makes the WinForms folder picker (System.Windows.Forms) use the
        // modern Vista-style dialog instead of the legacy one.
        System.Windows.Forms.Application.EnableVisualStyles();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Manual dependency composition (no external DI package needed).
        IGameLibraryService library = new GameLibraryService();
        ITempCleanerService tempCleaner = new TempCleanerService();
        IGameLauncherService launcher = new GameLauncherService();
        IDialogService dialogs = new DialogService();

        var viewModel = new MainViewModel(library, tempCleaner, launcher, dialogs);
        var mainWindow = new MainWindow(viewModel);

        MainWindow = mainWindow;
        mainWindow.Show();
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() => mainWindow.ShowFromTray()));
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Release the single-instance mutex only after the process is gone, so the
        // name stays owned for the whole lifetime of the app.
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string details = DescribeException(e.Exception);

        System.Windows.MessageBox.Show(
            $"An unexpected error occurred:\n\n{details}",
            "GFno Launcher",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);

        e.Handled = true;
    }

    /// <summary>
    /// Flattens the whole exception chain, outermost first, so the innermost cause is
    /// visible in the dialog. WPF wraps the real failure (for example
    /// <c>IOException: Cannot locate resource 'assets/icon.png'</c>) inside a generic
    /// <see cref="XamlParseException"/>, which on its own does not say what is wrong.
    /// </summary>
    private static string DescribeException(Exception exception)
    {
        var builder = new StringBuilder();

        int depth = 0;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (depth > 0)
            {
                builder.AppendLine();
            }

            builder.Append('(').Append(depth).Append(") ").AppendLine(current.GetType().FullName);
            builder.AppendLine(current.Message);

            if (current is XamlParseException xamlException && xamlException.LineNumber != 0)
            {
                builder.AppendLine($"   at XAML line {xamlException.LineNumber}, position {xamlException.LinePosition}");
            }

            depth++;
        }

        return builder.ToString().TrimEnd();
    }
}
