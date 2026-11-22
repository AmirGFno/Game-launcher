using System.Windows;
using System.Windows.Threading;

namespace GameLauncher.NativeServices;

/// <summary>
/// WPF / WinForms implementation of <see cref="INativeDialogService"/>.
/// <para>
/// Every call is marshalled to the UI thread: the bridge dispatches on thread-pool
/// threads, and native dialogs must be created on the thread that owns the window.
/// </para>
/// </summary>
public sealed class NativeDialogService : INativeDialogService
{
    private readonly Window _owner;
    private readonly Dispatcher _dispatcher;

    public NativeDialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dispatcher = owner.Dispatcher ?? throw new ArgumentNullException(nameof(owner));
    }

    public string? OpenFile(string filter, string? title = null)
    {
        return InvokeOnUi(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = title
            };

            return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
        });
    }

    public string? OpenFolder(string? description = null)
    {
        return InvokeOnUi(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description ?? "Select a folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            return dialog.ShowDialog(new WindowWrapper(_owner)) == System.Windows.Forms.DialogResult.OK
                ? dialog.SelectedPath
                : null;
        });
    }

    public void ShowError(string title, string message)
    {
        InvokeOnUi(() => System.Windows.MessageBox.Show(
            _owner,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error));
    }

    public void ShowInfo(string title, string message)
    {
        InvokeOnUi(() => System.Windows.MessageBox.Show(
            _owner,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information));
    }

    public bool Confirm(string title, string message)
    {
        return InvokeOnUi(() => System.Windows.MessageBox.Show(
            _owner,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, inlining when the caller is
    /// already there so handlers can be called from any thread.
    /// </summary>
    private T InvokeOnUi<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return action();
        }

        return _dispatcher.Invoke(action);
    }

    private void InvokeOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    /// <summary>
    /// Adapts a WPF window to <c>IWin32Window</c> for the WinForms folder dialog.
    /// </summary>
    private sealed class WindowWrapper : System.Windows.Forms.IWin32Window
    {
        public WindowWrapper(Window window)
        {
            Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }

        public IntPtr Handle { get; }
    }
}
