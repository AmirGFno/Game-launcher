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
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration TitleAnimationDuration = new(TimeSpan.FromMilliseconds(340));

    private readonly MainViewModel _viewModel;
    private readonly SemaphoreSlim _fadeGate = new(1, 1);
    private readonly SemaphoreSlim _titleGate = new(1, 1);
    private readonly DispatcherTimer _spinnerTimer;

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
        // The first background is shown without a transition.
        ShowInitialBackground(_viewModel.SelectedGame?.BackgroundPath);

        UpdateMaximizeIcon();
        _spinnerTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _spinnerTimer.Stop();
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedGame))
        {
            return;
        }

        string? path = _viewModel.SelectedGame?.BackgroundPath;

        // Background cross-fade and title slide-in run side by side.
        Task backgroundTransition = FadeBackgroundAsync(path);
        Task titleTransition = AnimateTitleAsync();

        await Task.WhenAll(backgroundTransition, titleTransition);

        if (_viewModel.SelectedGame is not null)
        {
            GamesListBox.ScrollIntoView(_viewModel.SelectedGame);
        }
    }

    #region Background transitions

    /// <summary>
    /// Fades the current background out, swaps the image and fades the new one in.
    /// A gate makes sure overlapping selection changes do not fight each other.
    /// </summary>
    private async Task FadeBackgroundAsync(string? path)
    {
        await _fadeGate.WaitAsync();

        try
        {
            await FadeAsync(0);
            SetBackground(path);
            await FadeAsync(1);
        }
        finally
        {
            _fadeGate.Release();
        }
    }

    /// <summary>Shows the very first background without any transition.</summary>
    private void ShowInitialBackground(string? path)
    {
        BackgroundImage.BeginAnimation(UIElement.OpacityProperty, null);
        BackgroundImage.Source = ImageLoader.Load(path);
        BackgroundImage.Opacity = 1;
    }

    /// <summary>Swaps the image while it is invisible (opacity 0).</summary>
    private void SetBackground(string? path)
    {
        BackgroundImage.BeginAnimation(UIElement.OpacityProperty, null);
        BackgroundImage.Source = ImageLoader.Load(path);
        BackgroundImage.Opacity = 0;
    }

    private async Task FadeAsync(double to)
    {
        if (Math.Abs(BackgroundImageOpacity - to) < 0.01)
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var animation = new DoubleAnimation(BackgroundImageOpacity, to, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) => completion.TrySetResult(true);
        BackgroundImage.BeginAnimation(UIElement.OpacityProperty, animation);

        // The Completed event is reliable, but never let a transition deadlock the UI.
        await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMilliseconds(800)));
    }

    private double BackgroundImageOpacity => BackgroundImage.Opacity;

    #endregion

    #region Title animation

    /// <summary>
    /// Slides the title up into place with a soft fade whenever the selection changes.
    /// </summary>
    private async Task AnimateTitleAsync()
    {
        await _titleGate.WaitAsync();

        try
        {
            TitlePanel.BeginAnimation(UIElement.OpacityProperty, null);
            TitlePanel.Opacity = 0;

            var transform = TitlePanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
            transform.Y = 14;
            TitlePanel.RenderTransform = transform;

            var storyboard = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TitleAnimationDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, TitlePanel);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

            var slide = new DoubleAnimation(14, 0, TitleAnimationDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, TitlePanel);
            Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.Y"));

            storyboard.Children.Add(fade);
            storyboard.Children.Add(slide);

            await RunStoryboardAsync(storyboard);
        }
        finally
        {
            _titleGate.Release();
        }
    }

    private static async Task RunStoryboardAsync(Storyboard storyboard)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        storyboard.Completed += (_, _) => completion.TrySetResult(true);
        storyboard.Begin();

        await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMilliseconds(900)));
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

            // Restore the "restore" glyph once the window goes back to its
            // normal size (the same shape doubles as the maximize glyph).

        // Maximized = two overlapping squares (restore), normal = one square.
        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M11,5 H19 V13 H17 V7 H11 Z M9,11 H17 V21 H9 Z"
                : "M6,6 H20 V20 H6 Z");
    }

    #endregion
}
