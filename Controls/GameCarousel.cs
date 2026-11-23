using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using GameLauncher.Models;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace GameLauncher.Controls;

/// <summary>Two native layouts; selection retargets current presentation values, never queues moves.</summary>
public sealed class GameCarousel : System.Windows.Controls.UserControl
{
    public const double LoopTileSize = 120; // tweak here
    public const double LoopGap = 10;       // tweak here
    public const double FixedTileSize = 140;
    public const double FixedGap = 30; // 15px either side, matching the original WPF row
    public static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(300);
    public static readonly DependencyProperty GamesProperty = DependencyProperty.Register(nameof(Games), typeof(IEnumerable), typeof(GameCarousel), new PropertyMetadata(null, GamesChanged));
    public static readonly DependencyProperty SelectedGameProperty = DependencyProperty.Register(nameof(SelectedGame), typeof(GameItem), typeof(GameCarousel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((GameCarousel)d).RenderSelection()));
    public static readonly DependencyProperty StyleModeProperty = DependencyProperty.Register(nameof(StyleMode), typeof(string), typeof(GameCarousel), new PropertyMetadata("Loop", (d, _) => ((GameCarousel)d).Reset()));
    public IEnumerable? Games { get => (IEnumerable?)GetValue(GamesProperty); set => SetValue(GamesProperty, value); }
    public GameItem? SelectedGame { get => (GameItem?)GetValue(SelectedGameProperty); set => SetCurrentValue(SelectedGameProperty, value); }
    public string StyleMode { get => (string)GetValue(StyleModeProperty); set => SetValue(StyleModeProperty, value); }
    public event EventHandler? LaunchRequested;
    private readonly Grid _root = new();
    private readonly Canvas _tiles = new() { ClipToBounds = true };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
    private readonly Dictionary<int, Grid> _visuals = new();
    private List<GameItem> _items = new();
    private int _selected = -1, _position;
    private bool Fixed => StyleMode == "FixedAnchor";
    public string DisplayedTitle => _title.Text;
    public GameCarousel()
    {
        Content = _root; Focusable = false;
        _root.Children.Add(_tiles); _root.Children.Add(_title);
        SizeChanged += (_, _) => Reset();
        Loaded += (_, _) => Reset();
        Unloaded += (_, _) => { _visuals.Clear(); _tiles.Children.Clear(); };
    }
    public static int Wrap(int index, int count) => count == 0 ? 0 : (index % count + count) % count;
    public static int Direction(int from, int to, int count, bool loop) => Math.Sign(Delta(from, to, count, loop));
    private static int Delta(int from, int to, int count, bool loop)
    {
        int delta = to - from;
        if (loop && Math.Abs(delta) > count / 2) delta -= Math.Sign(delta) * count;
        return delta;
    }
    private static void GamesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (GameCarousel)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= c.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged current) current.CollectionChanged += c.CollectionChanged;
        c.Reset();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Reset();
    public void RefreshArtwork() => Reset();
    private void Reset()
    {
        _items = Games?.Cast<GameItem>().ToList() ?? new();
        _visuals.Clear(); _tiles.Children.Clear(); _selected = -1; _position = 0;
        RenderSelection(false);
    }
    private IEasingFunction Ease() => Fixed
        ? new QuarticEase { EasingMode = EasingMode.EaseInOut }
        : new CubicEase { EasingMode = EasingMode.EaseOut }; // Loop has a snappier decelerating slide, not Fixed's symmetric curve.
    private void Animate(Animatable target, DependencyProperty property, double from, double to, bool animate)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, to);
        if (animate) target.BeginAnimation(property, new DoubleAnimation(from, to, TransitionDuration) { EasingFunction = Ease(), FillBehavior = FillBehavior.Stop });
    }
    private void RenderSelection(bool animate = true)
    {
        int index = _items.IndexOf(SelectedGame!);
        // Update text immediately, even if artwork is still decoding or a previous slide is in flight.
        _title.Text = SelectedGame?.Title ?? string.Empty;
        if (index < 0 || ActualWidth < 1) { _tiles.Children.Clear(); _visuals.Clear(); return; }
        bool loop = !Fixed && _items.Count > 5;
        int delta = _selected < 0 ? 0 : Delta(_selected, index, _items.Count, loop);
        if (_selected < 0) { _position = index; animate = false; } else _position += delta;
        _selected = index;
        double size = Fixed ? FixedTileSize : LoopTileSize;
        double pitch = size + (Fixed ? FixedGap : LoopGap);
        int visible = Fixed ? (int)Math.Ceiling(ActualWidth / pitch) + 2 : Math.Max(3, Math.Min(_items.Count, (int)(ActualWidth / pitch) - 1));
        double anchor = Fixed ? ActualWidth / 2 - size / 2 : 2 * pitch;
        // Reserve a complete slot on the right; the mask applies only to artwork, never to the title.
        double viewport = Fixed ? ActualWidth : Math.Min(ActualWidth - pitch, visible * pitch);
        _tiles.Width = Math.Max(0, viewport); _tiles.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        _tiles.OpacityMask = new LinearGradientBrush(new GradientStopCollection {
            new(Colors.Transparent, 0), new(Colors.White, Fixed ? .05 : .12),
            new(Colors.White, Fixed ? .95 : .84), new(Colors.Transparent, 1) }, 0);
        int left = Fixed ? -(visible / 2) : -2;
        int right = Fixed ? visible / 2 : visible - 3;
        var needed = new HashSet<int>();
        for (int slot = left - 1; slot <= right + 1; slot++)
        {
            int virtualIndex = _position + slot;
            int itemIndex = index + slot;
            if (!loop && (itemIndex < 0 || itemIndex >= _items.Count)) continue;
            GameItem game = _items[Wrap(itemIndex, _items.Count)];
            needed.Add(virtualIndex);
            if (!_visuals.TryGetValue(virtualIndex, out var tile))
            {
                tile = CreateTile(game, size);
                _visuals.Add(virtualIndex, tile); _tiles.Children.Add(tile);
                Canvas.SetLeft(tile, anchor + (slot + delta) * pitch - (pitch-size)/2);
            }
            double x = Canvas.GetLeft(tile);
            double destination = anchor + slot * pitch - (pitch-size)/2;
            tile.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(tile, destination);
            if (animate) tile.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, destination, TransitionDuration) { EasingFunction = Ease(), FillBehavior = FillBehavior.Stop });
            Canvas.SetTop(tile, Fixed ? 78 : 44);
            var art = (Grid)tile.Children[0];
            var scale = (ScaleTransform)art.RenderTransform;
            // Small Loop scale stays within the 130px pitch; Fixed has 30px breathing room.
            double factor = slot == 0 ? (Fixed ? 1.18 : 1.04) : (Fixed ? .92 : .96);
            Animate(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, factor, animate);
            Animate(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, factor, animate);
            if (Fixed)
                art.Effect = slot == 0 ? new DropShadowEffect { Color = Colors.White, BlurRadius = 28, ShadowDepth = 0, Opacity = .28 } : null;
            else art.Effect = null; // No effect object or glow brush exists in Loop mode.
            tile.Opacity = Fixed && slot != 0 ? .82 : 1;
        }
        foreach (int key in _visuals.Keys.Where(k => !needed.Contains(k)).ToArray()) { _tiles.Children.Remove(_visuals[key]); _visuals.Remove(key); }
        _title.HorizontalAlignment = Fixed ? System.Windows.HorizontalAlignment.Stretch : System.Windows.HorizontalAlignment.Left;
        _title.VerticalAlignment = VerticalAlignment.Top;
        _title.TextAlignment = Fixed ? TextAlignment.Center : TextAlignment.Left;
        _title.Margin = Fixed ? new Thickness(20, 16, 20, 0) : new Thickness(anchor + size + 8, 44 + size + 12, 20, 0);
        _title.MaxWidth = Fixed ? Math.Max(0, ActualWidth-40) : Math.Max(0, ActualWidth-anchor-size-28);
        _title.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, animate ? TransitionDuration : TimeSpan.Zero));
    }
    private Grid CreateTile(GameItem game, double size)
    {
        double pitch = size + (Fixed ? FixedGap : LoopGap);
        var art = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(.5,.5), RenderTransform = new ScaleTransform(1,1) };
        art.Children.Add(new TextBlock { Text = game.Title, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        art.Children.Add(new AsyncImage { Path = game.LogoPath, Width = size, Height = size, DecodeWidth = 320, Desaturate = false, Stretch = Stretch.Uniform,
            Clip = new RectangleGeometry(new Rect(0,0,size,size), Fixed ? 10 : 0, Fixed ? 10 : 0) });
        var hitbox = new Grid { Width = pitch, Height = size, Background = Brushes.Transparent, ToolTip = game.Title, Cursor = System.Windows.Input.Cursors.Hand };
        hitbox.Children.Add(art);
        System.Windows.Automation.AutomationProperties.SetName(hitbox,game.Title);
        hitbox.MouseLeftButtonDown += (_, e) => { SelectedGame = game; if(e.ClickCount == 2) LaunchRequested?.Invoke(this,EventArgs.Empty); e.Handled=true; };
        return hitbox;
    }
}
