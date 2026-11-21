using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameLauncher.Converters;
using GameLauncher.ViewModels;

namespace GameLauncher;

public partial class MainWindow : Window
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(280));

    private readonly MainViewModel _viewModel;
    private readonly SemaphoreSlim _fadeGate = new(1, 1);
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
        // First background is set without an animation.
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

        await FadeBackgroundAsync(_viewModel.SelectedGame?.BackgroundPath);

        if (_viewModel.SelectedGame is not null)
        {
            GamesListBox.ScrollIntoView(_viewModel.SelectedGame);
        }
    }

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

    private Task FadeAsync(double to)
    {
        if (Math.Abs(BackgroundImage.Opacity - to) < 0.01)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var animation = new DoubleAnimation(BackgroundImage.Opacity, to, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) => completion.TrySetResult(true);
        BackgroundImage.BeginAnimation(UIElement.OpacityProperty, animation);

        return completion.Task;
    }

    private void OnSpinnerTick(object? sender, EventArgs e)
    {
        if (Spinner.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = (rotate.Angle + 5) % 360;
        }
    }

    /// <summary>
    /// The carousel scrolls sideways, so the vertical mouse wheel is translated
    /// into a horizontal scroll.
    /// </summary>
    private void GamesListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        System.Windows.Controls.ScrollViewer? scrollViewer =
            FindVisualChild<System.Windows.Controls.ScrollViewer>(listBox);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
            {
                return match;
            }

            T? result = FindVisualChild<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.IsCleaning)
        {
            return;
        }

        if (e.Key == Key.Right)
        {
            _viewModel.MoveNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            _viewModel.MovePrevious();
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource))
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

        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M11,5 H19 V13 H17 V7 H11 Z M9,11 H17 V21 H9 Z"
                : "M6,6 H20 V20 H6 Z");
    }
}
