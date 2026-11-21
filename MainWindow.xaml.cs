using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameLauncher.Converters;
using GameLauncher.Models;
using GameLauncher.ViewModels;

namespace GameLauncher;

public partial class MainWindow : Window
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(240));
    private static readonly Duration TitleAnimationDuration = new(TimeSpan.FromMilliseconds(320));

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _spinnerTimer;

    // The two background layers: exactly one is on top (the last one faded in).
    private System.Windows.Controls.Image _frontLayer = null!;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

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
        ShowInitialBackground(_viewModel.SelectedGame?.BackgroundPath);

        // The library may already have loaded (and selected a game) before the
        // window subscribed, so animate the title here as well.
        if (_viewModel.SelectedGame is not null)
        {
            AnimateTitle();
        }

        UpdateMaximizeIcon();
        _spinnerTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _spinnerTimer.Stop();
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

        CrossFadeBackground(_viewModel.SelectedGame?.BackgroundPath);
        AnimateTitle();

        if (_viewModel.SelectedGame is not null)
        {
            GamesListBox.ScrollIntoView(_viewModel.SelectedGame);
        }
    }

    #region Background transitions

    /// <summary>
    /// True cross-fade between the two background layers, so the screen never goes
    /// dark between games. Interruptible: starting a new fade replaces whatever the
    /// previous one was doing, from the opacity values it reached so far.
    /// </summary>
    private void CrossFadeBackground(string? path)
    {
        System.Windows.Controls.Image incoming = ReferenceEquals(_frontLayer, BackgroundBack) ? BackgroundFront : BackgroundBack;
        System.Windows.Controls.Image outgoing = ReferenceEquals(_frontLayer, BackgroundBack) ? BackgroundBack : BackgroundFront;

        // Capture the values the previous animation reached before clearing it.
        double incomingFrom = incoming.Opacity;
        double outgoingFrom = outgoing.Opacity;

        incoming.BeginAnimation(UIElement.OpacityProperty, null);
        outgoing.BeginAnimation(UIElement.OpacityProperty, null);

        incoming.Source = ImageLoader.Load(path);
        incoming.Opacity = incomingFrom;
        outgoing.Opacity = outgoingFrom;

        // The layer that fades in must render on top of the one fading out.
        System.Windows.Controls.Panel.SetZIndex(incoming, 1);
        System.Windows.Controls.Panel.SetZIndex(outgoing, 0);

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
    private void ShowInitialBackground(string? path)
    {
        BackgroundBack.BeginAnimation(UIElement.OpacityProperty, null);
        BackgroundFront.BeginAnimation(UIElement.OpacityProperty, null);
        System.Windows.Controls.Panel.SetZIndex(BackgroundBack, 0);
        System.Windows.Controls.Panel.SetZIndex(BackgroundFront, 0);

        BackgroundBack.Source = ImageLoader.Load(path);
        BackgroundBack.Opacity = 1;
        BackgroundFront.Source = null;
        BackgroundFront.Opacity = 0;

        _frontLayer = BackgroundBack;
    }

    #endregion

    #region Title animation

    /// <summary>
    /// Slides the title up into place with a soft fade whenever the selection changes.
    /// Starts from the values a previous animation reached, so interrupting it mid-way
    /// does not make the title jump.
    /// </summary>
    private void AnimateTitle()
    {
        double opacityFrom = TitlePanel.Opacity;

        TitlePanel.BeginAnimation(UIElement.OpacityProperty, null);
        TitlePanel.Opacity = opacityFrom;

        var transform = TitlePanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        double offsetFrom = transform.Y;
        transform.Y = offsetFrom;
        TitlePanel.RenderTransform = transform;

        var storyboard = new Storyboard();

        var fade = new DoubleAnimation(opacityFrom, 1, TitleAnimationDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, TitlePanel);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

        var slide = new DoubleAnimation(offsetFrom, 0, TitleAnimationDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, TitlePanel);
        Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.Y"));

        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

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
            case Key.Right:
                _viewModel.MoveNext();
                e.Handled = true;
                break;

            case Key.Left:
                _viewModel.MovePrevious();
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.PlayCommand.Execute(null);
                e.Handled = true;
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

        if (e.Delta > 0)
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
}
