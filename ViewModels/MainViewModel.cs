using System.Collections.ObjectModel;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IGameLibraryService _library;
    private readonly ITempCleanerService _tempCleaner;
    private readonly IGameLauncherService _launcher;
    private readonly IDialogService _dialog;
    private readonly IProgress<string> _progress;

    private GameItem? _selectedGame;
    private bool _isCleaning;
    private bool _isReordering;
    private string _statusMessage = string.Empty;

    public MainViewModel(
        IGameLibraryService library,
        ITempCleanerService tempCleaner,
        IGameLauncherService launcher,
        IDialogService dialog)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _tempCleaner = tempCleaner ?? throw new ArgumentNullException(nameof(tempCleaner));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

        // Progress<T> captures the UI synchronization context, so the status
        // messages raised on the worker thread are marshalled back to the UI.
        _progress = new Progress<string>(message => StatusMessage = message);

        PlayCommand = new AsyncRelayCommand(PlayAsync, () => SelectedGame is not null && !IsCleaning);
        AddGameCommand = new RelayCommand(AddGame, () => !IsCleaning);
        EditGameCommand = new RelayCommand(EditGame, () => SelectedGame is not null && !IsCleaning);
        DeleteGameCommand = new RelayCommand(DeleteGame, () => SelectedGame is not null && !IsCleaning);
        ToggleReorderCommand = new RelayCommand(() => IsReordering = !IsReordering,
            () => SelectedGame is not null && !IsCleaning);
        CancelCommand = new RelayCommand(() => PlayCommand.Cancel());

        Games.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGames));
            RefreshCommands();
        };

        _ = LoadGamesAsync();
    }

    public ObservableCollection<GameItem> Games { get; } = new();

    public GameItem? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                if (value is null)
                {
                    // Nothing left to reorder.
                    IsReordering = false;
                }

                OnPropertyChanged(nameof(HasSelection));
                RefreshCommands();
            }
        }
    }

    /// <summary>
    /// While true, the Left/Right arrows (and the mouse wheel) move the selected
    /// game within the library instead of browsing the selection.
    /// </summary>
    public bool IsReordering
    {
        get => _isReordering;
        set
        {
            if (SetProperty(ref _isReordering, value))
            {
                OnPropertyChanged(nameof(ReorderHint));
                RefreshCommands();
            }
        }
    }

    /// <summary>Bottom-centre hint; changes while reorder mode is active.</summary>
    public string ReorderHint => IsReordering
        ? "Reorder mode — ← / → move the selected game      Esc — done"
        : "Enter / Double-click — Launch      Scroll — Browse";

    public bool IsCleaning
    {
        get => _isCleaning;
        set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                RefreshCommands();
            }
        }
    }

    /// <summary>Status text shown on the loading overlay ("Cleaning Temp files...").</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasGames => Games.Count > 0;

    public bool HasSelection => SelectedGame is not null;

    public AsyncRelayCommand PlayCommand { get; }

    public RelayCommand AddGameCommand { get; }

    public RelayCommand EditGameCommand { get; }

    public RelayCommand DeleteGameCommand { get; }

    public RelayCommand ToggleReorderCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>Moves the carousel selection one item to the right (wraps around).</summary>
    public void MoveNext()
    {
        if (Games.Count == 0)
        {
            return;
        }

        GameItem? selected = SelectedGame;
        int index = selected is null ? -1 : Games.IndexOf(selected);
        SelectedGame = Games[(index + 1) % Games.Count];
    }

    /// <summary>Moves the carousel selection one item to the left (wraps around).</summary>
    public void MovePrevious()
    {
        if (Games.Count == 0)
        {
            return;
        }

        GameItem? selected = SelectedGame;
        int index = selected is null ? 0 : Games.IndexOf(selected);
        SelectedGame = Games[(index - 1 + Games.Count) % Games.Count];
    }

    /// <summary>
    /// Physically moves the selected game <paramref name="delta"/> positions in the
    /// library (used by the arrow keys while reorder mode is active). The game stays
    /// selected, so repeated presses keep walking it along the list.
    /// </summary>
    public void ReorderSelected(int delta)
    {
        if (SelectedGame is null || Games.Count < 2 || delta == 0)
        {
            return;
        }

        int index = Games.IndexOf(SelectedGame);
        if (index < 0)
        {
            return;
        }

        int target = Math.Clamp(index + delta, 0, Games.Count - 1);
        if (target == index)
        {
            return;
        }

        Games.Move(index, target);
        SelectedGame = Games[target];
        _ = PersistAsync();
    }

    private async Task LoadGamesAsync()
    {
        try
        {
            IReadOnlyList<GameItem> games = await _library.LoadAsync();
            foreach (GameItem game in games)
            {
                Games.Add(game);
            }

            SelectedGame = Games.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Library could not be loaded", ex.Message);
        }
    }

    private async Task PlayAsync(CancellationToken cancellationToken)
    {
        GameItem? game = SelectedGame;
        if (game is null)
        {
            return;
        }

        IsCleaning = true;
        StatusMessage = "Cleaning Temp files...";

        try
        {
            var extraFolders = new List<string>();
            if (!string.IsNullOrWhiteSpace(game.CustomCacheFolderPath))
            {
                extraFolders.Add(game.CustomCacheFolderPath);
            }

            CleanupResult result = await _tempCleaner.CleanAsync(extraFolders, _progress, cancellationToken)
                                                 .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            StatusMessage = result.Skipped > 0
                ? $"Removed {result.Deleted} item(s), skipped {result.Skipped}."
                : $"Removed {result.Deleted} item(s).";

            await Task.Delay(400, cancellationToken).ConfigureAwait(true);

            StatusMessage = $"Launching {game.Title}...";
            _launcher.Launch(game);

            game.LastPlayed = DateTime.Now;
            await PersistAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Could not launch the game", ex.Message);
        }
        finally
        {
            IsCleaning = false;
            StatusMessage = string.Empty;
        }
    }

    private void AddGame()
    {
        var viewModel = new GameEditorViewModel(_dialog);
        GameItem? result = _dialog.ShowGameEditor(viewModel);
        if (result is null)
        {
            return;
        }

        Games.Add(result);
        SelectedGame = result;
        _ = PersistAsync();
    }

    private void EditGame()
    {
        if (SelectedGame is null)
        {
            return;
        }

        var viewModel = new GameEditorViewModel(_dialog, SelectedGame);
        GameItem? result = _dialog.ShowGameEditor(viewModel);
        if (result is null)
        {
            return;
        }

        int index = Games.IndexOf(SelectedGame);
        if (index >= 0)
        {
            Games[index] = result;
        }
        else
        {
            Games.Add(result);
        }

        SelectedGame = result;
        _ = PersistAsync();
    }

    private void DeleteGame()
    {
        if (SelectedGame is null)
        {
            return;
        }

        bool confirmed = _dialog.Confirm(
            "Delete game",
            $"'{SelectedGame.Title}' will be removed from your library.");

        if (!confirmed)
        {
            return;
        }

        int index = Games.IndexOf(SelectedGame);
        Games.Remove(SelectedGame);

        SelectedGame = Games.Count > 0 ? Games[Math.Min(index, Games.Count - 1)] : null;
        IsReordering = false;
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _library.SaveAsync(Games.ToList());
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Library could not be saved", ex.Message);
        }
    }

    private void RefreshCommands()
    {
        PlayCommand.RaiseCanExecuteChanged();
        AddGameCommand.RaiseCanExecuteChanged();
        EditGameCommand.RaiseCanExecuteChanged();
        DeleteGameCommand.RaiseCanExecuteChanged();
        ToggleReorderCommand.RaiseCanExecuteChanged();
    }
}
