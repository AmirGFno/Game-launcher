using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameLauncher.Converters;

namespace GameLauncher.Controls;

/// <summary>
/// An <see cref="System.Windows.Controls.Image"/> whose artwork is decoded on a
/// worker thread (so a huge image never blocks the UI) and cached by
/// <see cref="ImageLoader"/>. Bind <see cref="Path"/> instead of <c>Source</c>.
/// </summary>
public sealed class AsyncImage : System.Windows.Controls.Image
{
    private CancellationTokenSource? _pending;

    static AsyncImage()
    {
        PathProperty = DependencyProperty.Register(
            nameof(Path),
            typeof(string),
            typeof(AsyncImage),
            new FrameworkPropertyMetadata(null, OnPathChanged));

        DecodeWidthProperty = DependencyProperty.Register(
            nameof(DecodeWidth),
            typeof(int),
            typeof(AsyncImage),
            new FrameworkPropertyMetadata(0, OnPathChanged));

        DesaturateProperty = DependencyProperty.Register(
            nameof(Desaturate),
            typeof(bool),
            typeof(AsyncImage),
            new FrameworkPropertyMetadata(false, OnPathChanged));
    }

    public static readonly DependencyProperty PathProperty;
    public static readonly DependencyProperty DecodeWidthProperty;
    public static readonly DependencyProperty DesaturateProperty;

    /// <summary>File path (or URL-free absolute path) of the artwork to display.</summary>
    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>
    /// Maximum decode width; larger files are downscaled while decoding. 0 keeps
    /// the original size.
    /// </summary>
    public int DecodeWidth
    {
        get => (int)GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    /// <summary>Shows a greyscale version of the artwork (unselected carousel tiles).</summary>
    public bool Desaturate
    {
        get => (bool)GetValue(DesaturateProperty);
        set => SetValue(DesaturateProperty, value);
    }

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AsyncImage)d).LoadAsync();

    private async void LoadAsync()
    {
        // Cancel the previous request (deliberately not disposed: its token may
        // still be observed by the worker that is running it).
        _pending?.Cancel();

        var cancellation = new CancellationTokenSource();
        _pending = cancellation;

        string? path = Path;
        int maxWidth = DecodeWidth;
        bool desaturate = Desaturate;

        try
        {
            BitmapSource? image = await ImageLoader
                .LoadAsync(path, maxWidth, desaturate, cancellation.Token)
                .ConfigureAwait(false);

            if (cancellation.IsCancellationRequested || !IsCurrent(path, maxWidth, desaturate))
            {
                return;
            }

            // Source lives on the UI thread.
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (cancellation.IsCancellationRequested || !IsCurrent(path, maxWidth, desaturate))
                    {
                        return;
                    }

                    Source = image;
                },
                DispatcherPriority.Render);
        }
        catch (OperationCanceledException)
        {
            // A newer request replaced this one - nothing to do.
        }
        catch
        {
            // A failed load leaves the previous content in place; the UI thread is
            // the only place where Source may be touched, so nothing is done here.
        }
    }

    private bool IsCurrent(string? path, int maxWidth, bool desaturate)
        => Path == path && DecodeWidth == maxWidth && Desaturate == desaturate;
}
