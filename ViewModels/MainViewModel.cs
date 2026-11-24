using System.Collections.ObjectModel;
using System.Windows.Media;
using GameLauncher.Models;
using GameLauncher.Services;
using Color = System.Windows.Media.Color;

namespace GameLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IGameLibraryService _library;
    private readonly ITempCleanerService _tempCleaner;
    private readonly IGameLauncherService _launcher;
    private readonly IDialogService _dialog;
    private readonly IProgress<string> _progress;

    private CancellationTokenSource? _steamScan;
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
        CleanCacheCommand = new AsyncRelayCommand(CleanCacheAsync, () => !IsCleaning);
        CancelCommand = new RelayCommand(() => { PlayCommand.Cancel(); _steamScan?.Cancel(); });

        Games.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGames));
            RefreshCommands();
        };

        _ = LoadGamesAsync();
    }

    public LauncherSettings Settings { get; set; } = new();

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

    /// <summary>Optional, explicitly initiated cleanup of the app's own cache.</summary>
    public AsyncRelayCommand CleanCacheCommand { get; }

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
        int next = Settings.CarouselStyle == "Loop" && Games.Count > 5
            ? (index + 1) % Games.Count : Math.Min(index + 1, Games.Count - 1);
        SelectedGame = Games[next];
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
        int previous = Settings.CarouselStyle == "Loop" && Games.Count > 5
            ? (index - 1 + Games.Count) % Games.Count : Math.Max(0,index - 1);
        SelectedGame = Games[previous];
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

            // Sample the logo colours in the background so the glow never blocks
            // the UI, then persist whatever was computed.
            _ = EnsureDominantColorsAsync(games);
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Library could not be loaded", ex.Message);
        }
    }

    public async Task ImportSteamAsync(CancellationToken token, string? manualRoot = null)
    {
        if (IsCleaning) return;
        IsCleaning=true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _steamScan = cancellation;
        token = cancellation.Token;
        try
        {
            var scanner=new SteamScannerService();
            var found=await Task.Run(() => scanner.Scan(manualRoot),token);
            int count=0, missingArtwork=0, repaired=0;
            foreach(var steam in found)
            {
                token.ThrowIfCancellationRequested();
                var existing = Games.FirstOrDefault(g => SteamScannerService.AppIdFromTarget(g.LaunchTarget) == steam.AppId);
                bool logoValid = existing is not null && await IsValidArtworkAsync(existing.LogoPath);
                bool heroValid = existing is not null && await IsValidArtworkAsync(existing.BackgroundPath);
                if (existing is not null) existing.SteamInstallDirectory = steam.InstallDirectory;
                if (logoValid && heroValid) { await PersistAsync(); continue; }
                StatusMessage=$"Importing {steam.Name}…";
                var game=await scanner.CreateAsync(steam,token,_progress);
                if (game.LogoPath is null || game.BackgroundPath is null) missingArtwork++;
                if (existing is null) { Games.Add(game); count++; }
                else
                {
                    if (!logoValid) existing.LogoPath = game.LogoPath;
                    if (!heroValid) existing.BackgroundPath = game.BackgroundPath;
                    repaired++;
                }
                SelectedGame ??= existing ?? game;
                await PersistAsync();
            }
            _dialog.ShowInfo("Steam scan", $"Added {count} games; retried artwork for {repaired} existing games. {missingArtwork} games still have unavailable artwork. Existing metadata and manual artwork were preserved.");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { _dialog.ShowError("Steam scan failed",ex.Message); }
        finally { _steamScan=null; IsCleaning=false; StatusMessage=string.Empty; }
    }

    private static Task<bool> IsValidArtworkAsync(string? path) => Task.Run(() =>
    {
        try { if (path is null) return false; ImageDownloader.ValidateImageFile(path); return true; }
        catch { return false; }
    });

    public Func<GameItem, Task>? PrepareFullscreenLaunchAsync { get; set; }
    public event EventHandler<bool>? FullscreenLaunchFinished;

    private async Task PlayAsync(CancellationToken cancellationToken)
    {
        GameItem? game = SelectedGame;
        if (game is null)
        {
            return;
        }

        // Hide chrome before starting, observe readiness asynchronously, then yield the screen.
        // Manual process cleanup is never part of the optional prelaunch cache cleanup.
        IsCleaning = true;
        StatusMessage = $"Launching {game.Title}...";
        bool started = false;
        try
        {
            if (PrepareFullscreenLaunchAsync is not null)
                await PrepareFullscreenLaunchAsync(game);
            if (Settings.CleanCacheBeforeLaunch)
                await _tempCleaner.CleanAsync(_progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await _launcher.LaunchAndWaitAsync(game, cancellationToken);
            started = true;
            FullscreenLaunchFinished?.Invoke(this, true);

            game.LastPlayed = DateTime.Now;
            game.LaunchCount++;
            await PersistAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            if (!started) { IsCleaning = false; FullscreenLaunchFinished?.Invoke(this, false); }
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            if (!started) { IsCleaning = false; FullscreenLaunchFinished?.Invoke(this, false); }
            _dialog.ShowError("Could not launch the game", ex.Message);
        }
        finally
        {
            IsCleaning = false;
            StatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// Optional cleanup of the application's own cache. Shows a preview and asks for
    /// confirmation first - nothing is ever deleted automatically.
    /// </summary>
    private async Task CleanCacheAsync(CancellationToken cancellationToken)
    {
        CleanupPreview preview;
        try
        {
            preview = await _tempCleaner.PreviewAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Cache cleanup failed", ex.Message);
            return;
        }

        double megabytes = preview.TotalBytes / (1024.0 * 1024.0);
        bool confirmed = _dialog.Confirm(
            "Clean cache",
            "Temp will be cleaned and processes launched by GFno may be closed. Continue?\n\n" +
            $"Delete {preview.EntryCount} item(s) ({megabytes:0.0} MB) from the GFno Launcher cache?\n\n" +
            $"Only the app's own cache folder is touched:\n{_tempCleaner.CacheDirectory}");

        if (!confirmed)
        {
            return;
        }

        IsCleaning = true;
        StatusMessage = "Cleaning cache...";

        try
        {
            CleanupResult result = await _tempCleaner
                .CleanAsync(_progress, cancellationToken)
                .ConfigureAwait(true);

            string processes = await _launcher.CloseTrackedAsync(cancellationToken);
            _dialog.ShowInfo(
                "Cache cleaned",
                $"Removed {result.Deleted} item(s), skipped {result.Skipped}.\n{processes}");
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user.
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Cache cleanup failed", ex.Message);
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
        _ = RefreshDominantColorAsync(result);
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
        _ = RefreshDominantColorAsync(result);
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

    /// <summary>
    /// Computes and caches the dominant glow colour for every logo, off the UI
    /// thread. Games whose colour was persisted already are skipped, so the same
    /// image is never sampled twice.
    /// </summary>
    private async Task EnsureDominantColorsAsync(IEnumerable<GameItem> games)
    {
        foreach (GameItem game in games)
        {
            // The caller is the UI thread, so the continuation that writes the
            // colour back onto the model also runs there.
            await RefreshDominantColorAsync(game);
        }

        _ = PersistAsync();
    }

    /// <summary>
    /// Extracts the dominant colour of a single game's logo on a background thread
    /// and stores it on the model, where the glow binding picks it up. The logo is
    /// decoded downscaled (256 px) and the result is reused on later starts.
    /// </summary>
    private async Task RefreshDominantColorAsync(GameItem? game)
    {
        if (game is null)
        {
            return;
        }

        if (game.RestoreDominantColor())
        {
            return;
        }

        try
        {
            Color color = await ImageColorExtractor
                .ExtractDominantColorAsync(game.LogoPath);

            game.SetDominantColor(color);
        }
        catch
        {
            // A colour is decorative - a broken image must never take the app down.
        }
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
        CleanCacheCommand.RaiseCanExecuteChanged();
    }
}
