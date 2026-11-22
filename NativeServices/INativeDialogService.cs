namespace GameLauncher.NativeServices;

/// <summary>
/// Native Windows dialogs the bridge exposes to the frontend.
/// <para>
/// This replaces the old WPF <c>IDialogService</c>: the add/edit game window is now
/// a React view, so the only UI the desktop host owns is the native file picker and
/// the message boxes.
/// </para>
/// </summary>
public interface INativeDialogService
{
    /// <summary>Shows a native file picker. Returns the selected path or <c>null</c>.</summary>
    string? OpenFile(string filter, string? title = null);

    /// <summary>Shows a native folder picker. Returns the selected path or <c>null</c>.</summary>
    string? OpenFolder(string? description = null);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    bool Confirm(string title, string message);
}
