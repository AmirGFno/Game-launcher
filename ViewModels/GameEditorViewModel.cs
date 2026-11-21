using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.ViewModels;

/// <summary>ViewModel of the Add / Edit game dialog.</summary>
public sealed class GameEditorViewModel : ObservableObject
{
    private readonly IDialogService _dialog;
    private readonly Guid _id;

    private string _title = string.Empty;
    private string _launchTarget = string.Empty;
    private string? _logoPath;
    private string? _backgroundPath;
    private string? _customCacheFolderPath;
    private string? _error;

    // Remember which values came from the executable, so that picking a different
    // .exe refreshes them while anything the user typed by hand is left alone.
    private string? _autoTitle;
    private string? _autoLogo;

    public GameEditorViewModel(IDialogService dialog, GameItem? existing = null)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

        WindowTitle = existing is null ? "Add Game" : "Edit Game";

        if (existing is not null)
        {
            _id = existing.Id;
            _title = existing.Title;
            _launchTarget = existing.LaunchTarget;
            _logoPath = existing.LogoPath;
            _backgroundPath = existing.BackgroundPath;
            _customCacheFolderPath = existing.CustomCacheFolderPath;
        }
        else
        {
            _id = Guid.NewGuid();
        }

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(this, false));
        BrowseLaunchTargetCommand = new RelayCommand(BrowseLaunchTarget);
        BrowseLogoCommand = new RelayCommand(BrowseLogo);
        BrowseBackgroundCommand = new RelayCommand(BrowseBackground);
        BrowseCacheFolderCommand = new RelayCommand(BrowseCacheFolder);
        DownloadLogoCommand = new AsyncRelayCommand(DownloadLogoAsync);
        DownloadBackgroundCommand = new AsyncRelayCommand(DownloadBackgroundAsync);
    }

    /// <summary>Raised when the dialog should close. The event argument is the dialog result.</summary>
    public event EventHandler<bool>? RequestClose;

    public string WindowTitle { get; }

    /// <summary>The game created by a successful save.</summary>
    public GameItem? Result { get; private set; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string LaunchTarget
    {
        get => _launchTarget;
        set => SetProperty(ref _launchTarget, value);
    }

    public string? LogoPath
    {
        get => _logoPath;
        set => SetProperty(ref _logoPath, value);
    }

    public string? BackgroundPath
    {
        get => _backgroundPath;
        set => SetProperty(ref _backgroundPath, value);
    }

    public string? CustomCacheFolderPath
    {
        get => _customCacheFolderPath;
        set => SetProperty(ref _customCacheFolderPath, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand BrowseLaunchTargetCommand { get; }

    public RelayCommand BrowseLogoCommand { get; }

    public RelayCommand BrowseBackgroundCommand { get; }

    public RelayCommand BrowseCacheFolderCommand { get; }

    /// <summary>Downloads the Logo URL currently in the text box.</summary>
    public AsyncRelayCommand DownloadLogoCommand { get; }

    /// <summary>Downloads the Background URL currently in the text box.</summary>
    public AsyncRelayCommand DownloadBackgroundCommand { get; }

    private void Save()
    {
        Error = Validate();
        if (Error is not null)
        {
            OnPropertyChanged(nameof(Error));
            return;
        }

        Result = new GameItem
        {
            Id = _id,
            Title = Title.Trim(),
            LaunchTarget = LaunchTarget.Trim(),
            LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath!.Trim(),
            BackgroundPath = string.IsNullOrWhiteSpace(BackgroundPath) ? null : BackgroundPath!.Trim(),
            CustomCacheFolderPath = string.IsNullOrWhiteSpace(CustomCacheFolderPath) ? null : CustomCacheFolderPath!.Trim()
        };

        RequestClose?.Invoke(this, true);
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return "Please enter a game title.";
        }

        if (string.IsNullOrWhiteSpace(LaunchTarget))
        {
            return "Please enter a launch target (.exe path or steam:// URL).";
        }

        if (!GameTarget.IsValid(LaunchTarget))
        {
            return "The launch target must be an .exe file path or a URI such as steam://rungameid/12345.";
        }

        return null;
    }

    private void BrowseLaunchTarget()
    {
        string? path = _dialog.OpenFile("Executable files (*.exe)|*.exe|All files (*.*)|*.*");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        LaunchTarget = path;

        // Auto-extract the display name and the embedded icon. Values the user
        // typed manually are never overwritten.
        string extractedTitle = GameInfoExtractor.GetDisplayName(path);
        string? extractedLogo = GameInfoExtractor.ExtractLogo(path);

        bool titleIsAuto = Title == _autoTitle;
        bool logoIsAuto = LogoPath == _autoLogo;

        if (string.IsNullOrWhiteSpace(Title) || titleIsAuto)
        {
            Title = extractedTitle;
            _autoTitle = extractedTitle;
        }

        if (string.IsNullOrWhiteSpace(LogoPath) || logoIsAuto)
        {
            LogoPath = extractedLogo;
            _autoLogo = extractedLogo;
        }
    }

    private void BrowseLogo()
    {
        string? path = _dialog.OpenFile("Images (*.ico;*.png;*.jpg;*.jpeg;*.webp)|*.ico;*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*");
        if (!string.IsNullOrEmpty(path))
        {
            LogoPath = path;
        }
    }

    private void BrowseBackground()
    {
        string? path = _dialog.OpenFile("Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*");
        if (!string.IsNullOrEmpty(path))
        {
            BackgroundPath = path;
        }
    }

    private void BrowseCacheFolder()
    {
        string? folder = _dialog.OpenFolder();
        if (!string.IsNullOrEmpty(folder))
        {
            CustomCacheFolderPath = folder;
        }
    }

    private Task DownloadLogoAsync(CancellationToken cancellationToken)
    {
        return DownloadImageAsync(LogoPath, path =>
        {
            LogoPath = path;
            _autoLogo = null;
        }, cancellationToken);
    }

    private Task DownloadBackgroundAsync(CancellationToken cancellationToken)
    {
        return DownloadImageAsync(BackgroundPath, path => BackgroundPath = path, cancellationToken);
    }

    /// <summary>
    /// The user pastes a direct image URL into the field and presses Download; the
    /// image is fetched asynchronously and the field is replaced with the local path.
    /// </summary>
    private async Task DownloadImageAsync(string? url, Action<string> setPath, CancellationToken cancellationToken)
    {
        try
        {
            string savedPath = await ImageDownloader.DownloadAsync(url, cancellationToken).ConfigureAwait(true);
            setPath(savedPath);
        }
        catch (OperationCanceledException)
        {
            // Cancelled - nothing to report.
        }
        catch (Exception ex)
        {
            _dialog.ShowError("Download failed", ex.Message);
        }
    }
}
