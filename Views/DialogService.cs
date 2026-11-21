using System.Windows;
using System.Windows.Forms;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.ViewModels;

namespace GameLauncher.Views;

/// <summary>WPF implementation of <see cref="IDialogService"/>.</summary>
public sealed class DialogService : IDialogService
{
    public string? OpenFile(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public string? OpenFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder that should be cleaned before the game starts",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        // FolderBrowserDialog is a WinForms dialog: its ShowDialog overload takes
        // an IWin32Window rather than a WPF Window, so the parameterless overload is used.
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    public void ShowError(string title, string message)
    {
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowInfo(string title, string message)
    {
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public bool Confirm(string title, string message)
    {
        return System.Windows.MessageBox.Show(
                   System.Windows.Application.Current?.MainWindow,
                   message,
                   title,
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public GameItem? ShowGameEditor(GameEditorViewModel viewModel)
    {
        var window = new GameEditorWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? viewModel.Result : null;
    }
}
