using GameLauncher.Models;
using GameLauncher.ViewModels;

namespace GameLauncher.Services;

/// <summary>
/// UI-facing operations used by the view models (dialogs, file/folder pickers).
/// Keeping this behind an interface lets the view models stay unit-testable.
/// </summary>
public interface IDialogService
{
    string? OpenFile(string filter);

    string? OpenFolder();

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    bool Confirm(string title, string message);

    /// <summary>
    /// Opens the add/edit game dialog. Returns the edited <see cref="GameItem"/>,
    /// or <c>null</c> when the user cancelled.
    /// </summary>
    GameItem? ShowGameEditor(GameEditorViewModel viewModel);
}
