using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using GameLauncher.Models;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace GameLauncher.Controls;

/// <summary>Bounded visual window. Wrapping recycles tiles only after they leave the viewport.</summary>
public sealed class GameCarousel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty GamesProperty = DependencyProperty.Register(nameof(Games), typeof(IEnumerable), typeof(GameCarousel), new PropertyMetadata(null, GamesChanged));
    public static readonly DependencyProperty SelectedGameProperty = DependencyProperty.Register(nameof(SelectedGame), typeof(GameItem), typeof(GameCarousel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SelectionChanged));
    public static readonly DependencyProperty StyleModeProperty = DependencyProperty.Register(nameof(StyleMode), typeof(string), typeof(GameCarousel), new PropertyMetadata("Loop", (d, _) => ((GameCarousel)d).Reset()));
    public IEnumerable? Games { get => (IEnumerable?)GetValue(GamesProperty); set => SetValue(GamesProperty, value); }
    public GameItem? SelectedGame { get => (GameItem?)GetValue(SelectedGameProperty); set => SetValue(SelectedGameProperty, value); }
    public string StyleMode { get => (string)GetValue(StyleModeProperty); set { SetValue(StyleModeProperty, value); Reset(); } }
    public event EventHandler? LaunchRequested;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private int _shown = -1, _generation;
    private bool _moving;
    private List<GameItem> Items => Games?.Cast<GameItem>().ToList() ?? new();
    public GameCarousel()
    {
        Content = _canvas;
        Focusable = false;
        _canvas.OpacityMask = new LinearGradientBrush(new GradientStopCollection {
            new(Colors.Transparent, 0), new(Colors.White, .07), new(Colors.White, .93), new(Colors.Transparent, 1) }, 0);
        SizeChanged += (_, _) => Reset();
        Loaded += (_, _) => Reset();
        Unloaded += (_, _) => { _generation++; _moving = false; _canvas.Children.Clear(); };
    }
    public static int Wrap(int index, int count) => count == 0 ? 0 : (index % count + count) % count;
    public static int Direction(int from, int to, int count, bool loop)
    {
        int difference = to - from;
        if (loop && Math.Abs(difference) > count / 2) difference -= Math.Sign(difference) * count;
        return Math.Sign(difference);
    }
    private static void GamesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (GameCarousel)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged current) current.CollectionChanged += control.CollectionChanged;
        control.Reset();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Reset();
    private static void SelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((GameCarousel)d).Move();
    private void Reset()
    {
        _generation++; _moving = false;
        _shown = Items.IndexOf(SelectedGame!);
        Draw();
    }
    private double Pitch => Math.Max(100, ActualWidth / 6);
    private double Anchor => StyleMode == "FixedAnchor" ? 2 * Pitch : ActualWidth / 2;
    private bool Loops(int count) => StyleMode == "Loop" && count > 5;
    private void Draw()
    {
        _canvas.Children.Clear();
        var games = Items;
        if (_shown < 0 || games.Count == 0) return;
        // Extra slots stay outside the viewport while the entering/exiting tiles slide.
        for (int slot = -5; slot <= 6; slot++)
        {
            int index = _shown + slot;
            if (!Loops(games.Count) && (index < 0 || index >= games.Count)) continue;
            GameItem game = games[Wrap(index, games.Count)];
            double size = Math.Min(160, Pitch * .72);
            var image = new AsyncImage { Path = game.LogoPath, DecodeWidth = 320, Desaturate = false,
                Width = size, Height = size, Stretch = Stretch.UniformToFill,
                Clip = new RectangleGeometry(new Rect(0, 0, size, size), 16, 16) };
            var tile = new Grid { Width = size, Height = size, Tag = slot, RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new ScaleTransform(slot == 0 ? 1.24 : .88, slot == 0 ? 1.24 : .88),
                Effect = new DropShadowEffect { Color = Color.FromRgb(69,224,205), BlurRadius = 28, ShadowDepth = 0, Opacity = slot == 0 ? .6 : 0 } };
            tile.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromRgb(23,34,56)),
                Child = new TextBlock { Text = game.Title, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } });
            tile.Children.Add(image);
            tile.ToolTip = game.Title;
            tile.Cursor = System.Windows.Input.Cursors.Hand;
            System.Windows.Automation.AutomationProperties.SetName(tile, game.Title);
            tile.MouseLeftButtonDown += (_, e) => { SelectedGame = game; if (e.ClickCount == 2) LaunchRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };
            Canvas.SetLeft(tile, Anchor + slot * Pitch - size / 2);
            Canvas.SetTop(tile, 35);
            _canvas.Children.Add(tile);
        }
        var title = new Border { Tag = "title", BorderBrush = new SolidColorBrush(Color.FromRgb(69,224,205)), BorderThickness = new Thickness(3,0,0,0), Padding = new Thickness(12,2,0,2),
            Child = new TextBlock { Text = games[_shown].Title, FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 18, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Math.Max(100,ActualWidth - Anchor - 20) } };
        Canvas.SetLeft(title, Anchor - Math.Min(160, Pitch * .72) / 2);
        Canvas.SetTop(title, 35 + Math.Min(160, Pitch * .72) * 1.24 + 14);
        _canvas.Children.Add(title);
        title.BeginAnimation(OpacityProperty, new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(240)));
    }
    private void Move()
    {
        var games = Items;
        int target = games.IndexOf(SelectedGame!);
        if (_shown < 0 || target < 0 || ActualWidth == 0) { Reset(); return; }
        if (_moving || target == _shown) return;
        int direction = Direction(_shown,target,games.Count,Loops(games.Count));
        _moving = true;
        int generation = ++_generation;
        var duration = TimeSpan.FromMilliseconds(320);
        foreach (UIElement element in _canvas.Children)
        {
            if (element is not Grid tile) { element.BeginAnimation(OpacityProperty, new DoubleAnimation(0,duration)); continue; }
            int slot = (int)tile.Tag;
            var slide = new DoubleAnimation(Canvas.GetLeft(tile) - direction * Pitch, duration) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut } };
            tile.BeginAnimation(Canvas.LeftProperty, slide);
            double scale = slot - direction == 0 ? 1.24 : .88;
            var transform = (ScaleTransform)tile.RenderTransform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale,duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale,duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            ((DropShadowEffect)tile.Effect).BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(slot-direction == 0 ? .6 : 0,duration));
        }
        var clock = new DoubleAnimation(0,1,duration);
        clock.Completed += (_, _) => { if (generation != _generation) return; _shown = Wrap(_shown+direction,games.Count); _moving=false; Draw(); Move(); };
        BeginAnimation(TagProgressProperty,clock);
    }
    private static readonly DependencyProperty TagProgressProperty = DependencyProperty.Register("TagProgress",typeof(double),typeof(GameCarousel),new PropertyMetadata(0d));
}
