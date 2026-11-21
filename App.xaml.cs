using System.Windows;
using System.Windows.Threading;
using GameLauncher.Services;
using GameLauncher.ViewModels;
using GameLauncher.Views;

namespace GameLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "Game Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
