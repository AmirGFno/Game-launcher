using System.Windows;
using System.Windows.Threading;

namespace GameLauncher.WindowManagement;

/// <summary>The visual state of the launcher window.</summary>
public enum LauncherWindowState
{
    Normal,
    Minimized,
    Maximized,
    Fullscreen
}

/// <summary>
/// Owns the native window operations the bridge exposes: minimise, maximise /
/// restore, close and true full-screen.
/// <para>
/// The window itself is a borderless <c>WindowChrome</c> window, so the caption
/// buttons are rendered by React but the operations still go through WPF, which
/// keeps snap layouts, the system menu, DPI handling and taskbar behaviour native.
/// </para>
/// </summary>
public sealed class WindowController
{
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;

    private LauncherWindowState _state = LauncherWindowState.Normal;
    private WindowState _preFullscreenState = WindowState.Normal;
    private WindowStyle _preFullscreenStyle = WindowStyle.None;
    private bool _preFullscreenTopmost;
    private double _preFullscreenLeft;
    private double _preFullscreenTop;
    private double _preFullscreenWidth;
    private double _preFullscreenHeight;

    public WindowController(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dispatcher = window.Dispatcher ?? throw new ArgumentNullException(nameof(window));

        _window.StateChanged += OnWindowStateChanged;
    }

    /// <summary>Current state, safe to read from any thread.</summary>
    public LauncherWindowState State
    {
        get
        {
            if (_dispatcher.CheckAccess())
            {
                return _state;
            }

            return _dispatcher.Invoke(() => _state);
        }
    }

    /// <summary>
    /// Raised whenever the state changes, including changes the user made with the
    /// keyboard or by dragging, so the frontend can keep its caption buttons honest.
    /// </summary>
    public event EventHandler<LauncherWindowState>? StateChanged;

    public void Minimize()
    {
        InvokeOnUi(() =>
        {
            _window.WindowState = WindowState.Minimized;
            SetState(LauncherWindowState.Minimized);
        });
    }

    /// <summary>Toggles between maximised and restored.</summary>
    public void MaximizeOrRestore()
    {
        InvokeOnUi(() =>
        {
            if (_state == LauncherWindowState.Fullscreen)
            {
                SetFullscreen(false);
                return;
            }

            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            SetState(_window.WindowState == WindowState.Maximized
                ? LauncherWindowState.Maximized
                : LauncherWindowState.Normal);
        });
    }

    public void Restore()
    {
        InvokeOnUi(() =>
        {
            _window.WindowState = WindowState.Normal;
            SetState(LauncherWindowState.Normal);
        });
    }

    public void Close()
    {
        InvokeOnUi(() => _window.Close());
    }

    /// <summary>
    /// True full-screen: covers the taskbar, hides the caption and keeps the
    /// previous geometry so restoring is exact.
    /// </summary>
    public void SetFullscreen(bool fullscreen)
    {
        InvokeOnUi(() =>
        {
            if (fullscreen)
            {
                if (_state == LauncherWindowState.Fullscreen)
                {
                    return;
                }

                _preFullscreenState = _window.WindowState;
                _preFullscreenStyle = _window.WindowStyle;
                _preFullscreenTopmost = _window.Topmost;
                _preFullscreenLeft = _window.Left;
                _preFullscreenTop = _window.Top;
                _preFullscreenWidth = _window.Width;
                _preFullscreenHeight = _window.Height;

                _window.WindowStyle = WindowStyle.None;
                _window.WindowState = WindowState.Normal;
                _window.Topmost = true;
                _window.Left = 0;
                _window.Top = 0;
                _window.Width = SystemParameters.PrimaryScreenWidth;
                _window.Height = SystemParameters.PrimaryScreenHeight;

                SetState(LauncherWindowState.Fullscreen);
                return;
            }

            if (_state != LauncherWindowState.Fullscreen)
            {
                return;
            }

            _window.Topmost = _preFullscreenTopmost;
            _window.Left = _preFullscreenLeft;
            _window.Top = _preFullscreenTop;
            _window.Width = _preFullscreenWidth;
            _window.Height = _preFullscreenHeight;
            _window.WindowStyle = _preFullscreenStyle;
            _window.WindowState = _preFullscreenState;

            SetState(_window.WindowState == WindowState.Maximized
                ? LauncherWindowState.Maximized
                : LauncherWindowState.Normal);
        });
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // The user may have minimised or restored with the keyboard; the frontend
        // still needs to know.
        if (_state == LauncherWindowState.Fullscreen)
        {
            return;
        }

        SetState(_window.WindowState switch
        {
            WindowState.Minimized => LauncherWindowState.Minimized,
            WindowState.Maximized => LauncherWindowState.Maximized,
            _ => LauncherWindowState.Normal
        });
    }

    private void SetState(LauncherWindowState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, state);
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
}
