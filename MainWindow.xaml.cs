using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using GameLauncher.Converters;
using GameLauncher.Models;
using GameLauncher.ViewModels;
using GameLauncher.Services;
using GameLauncher.Views;

namespace GameLauncher;

public partial class MainWindow : Window
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(240));
    private static readonly Duration TitleAnimationDuration = new(TimeSpan.FromMilliseconds(320));

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _spinnerTimer;

    // The two background layers: exactly one is on top (the last one faded in).
    private System.Windows.Controls.Image _frontLayer = null!;

    // Incremented on every background request so a slow decode started for a game
    // the user has already left cannot overwrite the newer transition.
    private int _backgroundGeneration;

    // System-tray icon: closing the window hides it instead of quitting.
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isQuitting;
    private readonly LauncherSettingsService _settingsService = new();
    private LauncherSettings _settings = new();
    private WindowState _beforeFullscreen;
    private Rect _windowedBounds;
    private bool _windowedTopmost;
    private readonly CancellationTokenSource _scanCancellation = new();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        _settings = _settingsService.Load();
        _viewModel.Settings = _settings;
        Carousel.LaunchRequested += (_, _) => { if (!_viewModel.IsReordering) _viewModel.PlayCommand.Execute(null); };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _spinnerTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _spinnerTimer.Tick += OnSpinnerTick;

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _frontLayer = BackgroundBack;
        _ = ShowInitialBackgroundAsync(_viewModel.SelectedGame?.BackgroundPath);

        // The library may already have loaded (and selected a game) before the
        // window subscribed, so animate the title here as well.
        if (_viewModel.SelectedGame is not null)
        {
            AnimateTitle();
        }

        UpdateMaximizeIcon();
        _spinnerTimer.Start();
        EnsureTrayIcon();
        ApplySettings();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _spinnerTimer.Stop();
        _scanCancellation.Cancel();
        DisposeTrayIcon();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window is never really closed: it just hides to the system tray.
        // Only the tray's "Quit" entry (or a real shutdown) ends the process.
        // Without a tray icon there would be no way back, so fall back to a real
        // close in that (unlikely) case.
        if (!_isQuitting && _settings.CloseToTray && _trayIcon is not null)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        DisposeTrayIcon();
        base.OnClosing(e);
    }

    /// <summary>
    /// Selection changed: start the new transition immediately. Nothing is queued -
    /// a previously running fade/title animation is simply replaced, so rapid
    /// Left/Right navigation never falls behind the actual selection.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedGame))
        {
            return;
        }

        _ = CrossFadeBackgroundAsync(_viewModel.SelectedGame?.BackgroundPath);
        AnimateTitle();

        if (_viewModel.SelectedGame is not null)
        {
            // GameCarousel owns animated positioning; never ScrollIntoView across a wrap.
        }
    }

    #region Background transitions

    /// <summary>
    /// True cross-fade between the two background layers, so the screen never goes
    /// dark between games. The wallpaper is decoded off the UI thread and cached;
    /// a request started for a game the user has already left is discarded, so rapid
    /// Left/Right navigation never falls behind the actual selection.
    /// </summary>
    private async Task CrossFadeBackgroundAsync(string? path)
    {
        int generation = ++_backgroundGeneration;

        System.Windows.Controls.Image incoming = ReferenceEquals(_frontLayer, BackgroundBack) ? BackgroundFront : BackgroundBack;
        System.Windows.Controls.Image outgoing = ReferenceEquals(_frontLayer, BackgroundBack) ? BackgroundBack : BackgroundFront;

        // Capture the values the previous animation reached before clearing it.
        double incomingFrom = incoming.Opacity;
        double outgoingFrom = outgoing.Opacity;

        BitmapSource? image = await ImageLoader
            .LoadAsync(path, ImageLoader.MaxBackgroundWidth)
            .ConfigureAwait(true);

        // A newer navigation has taken over - leave the layers to it.
        if (generation != _backgroundGeneration)
        {
            return;
        }

        incoming.BeginAnimation(UIElement.OpacityProperty, null);
        outgoing.BeginAnimation(UIElement.OpacityProperty, null);

        incoming.Source = image;
        incoming.Opacity = incomingFrom;
        outgoing.Opacity = outgoingFrom;

        // The layer that fades in renders above the one fading out, but both stay
        // below the readability overlay (3) and the UI grid (4) - the wallpaper must
        // never climb above the interface.
        System.Windows.Controls.Panel.SetZIndex(incoming, 2);
        System.Windows.Controls.Panel.SetZIndex(outgoing, 1);

        var storyboard = new Storyboard();

        var fadeIn = new DoubleAnimation(incomingFrom, 1, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fadeIn, incoming);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

        var fadeOut = new DoubleAnimation(outgoingFrom, 0, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fadeOut, outgoing);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();

        _frontLayer = incoming;
    }

    /// <summary>Shows the very first background without any transition.</summary>
    private async Task ShowInitialBackgroundAsync(string? path)
    {
        int generation = ++_backgroundGeneration;

        BitmapSource? image = await ImageLoader
            .LoadAsync(path, ImageLoader.MaxBackgroundWidth)
            .ConfigureAwait(true);

        // A selection change already took over the background.
        if (generation != _backgroundGeneration)
        {
            return;
        }

        BackgroundBack.BeginAnimation(UIElement.OpacityProperty, null);
        BackgroundFront.BeginAnimation(UIElement.OpacityProperty, null);
        System.Windows.Controls.Panel.SetZIndex(BackgroundBack, 2);
        System.Windows.Controls.Panel.SetZIndex(BackgroundFront, 1);

        if (image is not null)
        {
            BackgroundBack.Source = image;
        }

        BackgroundBack.Opacity = 1;
        BackgroundFront.Source = null;
        BackgroundFront.Opacity = 0;

        _frontLayer = BackgroundBack;
    }

    #endregion

    #region System tray

    /// <summary>
    /// Creates the notify icon used to keep the launcher running in the background.
    /// Created from <c>Loaded</c> so the dispatcher loop is already running.
    /// </summary>
    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        try
        {
            System.Drawing.Icon? icon = LoadTrayIcon();
            if (icon is null)
            {
                return;
            }

            var tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "GFno Launcher",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open GFno Launcher", null, new EventHandler((_, _) => ShowFromTray()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quit", null, new EventHandler((_, _) => QuitFromTray()));
            tray.ContextMenuStrip = menu;

            tray.MouseDoubleClick += (_, _) => ShowFromTray();
            tray.MouseClick += (_, e) =>
            {
                // Some shells only raise MouseClick, so treat a double click there too.
                if (e.Clicks == 2)
                {
                    ShowFromTray();
                }
            };

            _trayIcon = tray;
        }
        catch
        {
            // No tray icon - the launcher still works, it just cannot hide to tray.
        }
    }

    internal void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void QuitFromTray()
    {
        _isQuitting = true;
        DisposeTrayIcon();
        System.Windows.Application.Current.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    /// <summary>
    /// The tray icon comes from the executable's own icon (the same
    /// <c>Assets/icon.ico</c> the compiler embeds), with the embedded resource as a
    /// fallback for single-file publishes.
    /// </summary>
    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            string? executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(executable) && File.Exists(executable))
            {
                using System.Drawing.Icon? extracted = System.Drawing.Icon.ExtractAssociatedIcon(executable);
                if (extracted is not null)
                {
                    return new System.Drawing.Icon(extracted, extracted.Width, extracted.Height);
                }
            }
        }
        catch
        {
            // Fall through to the embedded resource.
        }

        try
        {
            Stream? stream = System.Reflection.Assembly.GetEntryAssembly()?
                .GetManifestResourceStream("GameLauncher.Assets.icon.ico");
            if (stream is not null)
            {
                return new System.Drawing.Icon(stream);
            }
        }
        catch
        {
            // No tray icon - the launcher still works, it just cannot hide to tray.
        }

        return null;
    }

    #endregion

    #region Title animation

    /// <summary>
    /// Slides the title up into place with a soft fade whenever the selection changes.
    /// Starts from the values a previous animation reached, so interrupting it mid-way
    /// does not make the title jump.
    /// </summary>
    private void AnimateTitle() { /* The selected tile's title is animated inside GameCarousel. */ }

    #endregion

    #region Input

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.IsCleaning)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F11:
                _settings.Fullscreen = !_settings.Fullscreen;
                ApplySettings();
                try { _settingsService.Save(_settings); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message); }
                e.Handled = true;
                break;
            case Key.Right:
                if (_viewModel.IsReordering)
                {
                    _viewModel.ReorderSelected(1);
                }
                else
                {
                    _viewModel.MoveNext();
                }

                e.Handled = true;
                break;

            case Key.Left:
                if (_viewModel.IsReordering)
                {
                    _viewModel.ReorderSelected(-1);
                }
                else
                {
                    _viewModel.MovePrevious();
                }

                e.Handled = true;
                break;

            case Key.Escape:
                if (_viewModel.IsReordering)
                {
                    _viewModel.IsReordering = false;
                    e.Handled = true;
                }

                break;

            case Key.Enter:
                // While reordering, Enter must not launch the game by accident.
                if (!_viewModel.IsReordering)
                {
                    _viewModel.PlayCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    /// <summary>
    /// The mouse wheel browses through the library (the selected tile is always
    /// scrolled into view).
    /// </summary>
    private void GamesListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel.IsCleaning)
        {
            return;
        }

        if (_viewModel.IsReordering)
        {
            // In reorder mode the wheel moves the selected game, like the arrows.
            _viewModel.ReorderSelected(e.Delta > 0 ? -1 : 1);
        }
        else if (e.Delta > 0)
        {
            _viewModel.MovePrevious();
        }
        else if (e.Delta < 0)
        {
            _viewModel.MoveNext();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Double-clicking a tile launches that game. The click count is used instead of a
    /// <c>MouseDoubleClick</c> handler because that event lives on <c>Control</c>, and a
    /// plain <c>Grid</c> is not a <c>Control</c>.
    /// </summary>
    private void GameTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount < 2 || _viewModel.IsCleaning)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button)
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is GameItem game)
        {
            _viewModel.SelectedGame = game;
        }

        _viewModel.PlayCommand.Execute(null);
    }

    #endregion

    #region Window chrome

    private void OnSpinnerTick(object? sender, EventArgs e)
    {
        if (Spinner.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = (rotate.Angle + 5) % 360;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // Double-click on the title bar maximizes / restores the window.
        // (Grid has no MouseDoubleClick event, so the click count is used instead.)
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        // Both glyphs are 16x16: a single square when normal, two overlapping
        // squares (restore) when maximized.
        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M6.5,3 H13 V9.5 H6.5 Z M3,6.5 H9.5 V13 H3 Z"
                : "M3,3 H13 V13 H3 Z");
    }

    #endregion
    private bool _fullscreenApplied;
    private void ApplySettings()
    {
        _viewModel.Settings = _settings;
        Carousel.StyleMode = _settings.CarouselStyle;
        if (_settings.Fullscreen && !_fullscreenApplied)
        {
            _beforeFullscreen = WindowState;
            _windowedBounds = WindowState == WindowState.Normal
                ? new Rect(Left,Top,ActualWidth,ActualHeight) : RestoreBounds;
            _windowedTopmost = Topmost;
            var monitor = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            Left = monitor.Bounds.Left / dpi.DpiScaleX;
            Top = monitor.Bounds.Top / dpi.DpiScaleY;
            Width = monitor.Bounds.Width / dpi.DpiScaleX;
            Height = monitor.Bounds.Height / dpi.DpiScaleY;
            Topmost = true;
        }
        else if (!_settings.Fullscreen && _fullscreenApplied)
        {
            WindowState = WindowState.Normal;
            Topmost = _windowedTopmost;
            Left = _windowedBounds.Left; Top = _windowedBounds.Top;
            Width = _windowedBounds.Width; Height = _windowedBounds.Height;
            WindowState = _beforeFullscreen;
            ResizeMode = ResizeMode.NoResize;
        }
        _fullscreenApplied = _settings.Fullscreen;
        HeaderBar.Visibility = _settings.Fullscreen ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = new GridLength(_settings.Fullscreen ? 0 : 64);
    }
    private void Settings_Click(object sender,RoutedEventArgs e)
    {
        if (_viewModel.IsCleaning) return;
        var dialog = new SettingsWindow(_settings,_settingsService) { Owner=this };
        if (dialog.ShowDialog()==true) { _settings=dialog.Settings; ApplySettings(); }
        if (dialog.ArtworkCleared)
        {
            Carousel.RefreshArtwork();
            _ = CrossFadeBackgroundAsync(_viewModel.SelectedGame?.BackgroundPath);
        }
    }
    private async void SteamScan_Click(object sender,RoutedEventArgs e)
    {
        if (_viewModel.IsCleaning) return;
        if (System.Windows.MessageBox.Show(this,"Scan local Steam libraries and add missing games? Artwork is downloaded from Steam's CDN; existing entries are unchanged.","Steam scan",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        SteamButton.IsEnabled=false;
        try { await _viewModel.ImportSteamAsync(_scanCancellation.Token); }
        finally { SteamButton.IsEnabled=true; }
    }

}
