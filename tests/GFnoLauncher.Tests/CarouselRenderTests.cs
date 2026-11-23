using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using GameLauncher.Controls;
using GameLauncher.Models;
using Xunit;

namespace GFnoLauncher.Tests;

public class CarouselRenderTests
{
    [Fact]
    public async Task CircularSeamCompletesWithBoundedTilesAndSelectedGameIntact()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            try
            {
                var games = new ObservableCollection<GameItem>(Enumerable.Range(0,6).Select(i => new GameItem { Title="Game " + i }));
                var carousel = new GameCarousel { Width=1200, Height=290, Games=games, SelectedGame=games[5] };
                var selection = new GameCarousel { SelectedGame=games[5] };
                BindingOperations.SetBinding(carousel,GameCarousel.SelectedGameProperty,new Binding("SelectedGame") { Source=selection,Mode=BindingMode.TwoWay });
                var window = new Window { Width=1200, Height=350, ShowInTaskbar=false, WindowStyle=WindowStyle.None,
                    Left=-10000, Top=-10000, Content=carousel };
                window.Show();
                window.UpdateLayout();
                dispatcher.BeginInvoke(new Action(() => {
                    carousel.SelectedGame=games[0];
                    Assert.Equal("Game 0",carousel.DisplayedTitle);
                    carousel.SelectedGame=games[3];
                    Assert.Equal("Game 3",carousel.DisplayedTitle);
                    carousel.SelectedGame=games[0];
                }),DispatcherPriority.Loaded);
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        Assert.Same(games[0],carousel.SelectedGame);
                        Assert.True(BindingOperations.IsDataBound(carousel,GameCarousel.SelectedGameProperty));
                        Assert.Same(games[0],selection.SelectedGame);
                        var root=Assert.IsType<Grid>(carousel.Content);
                        var canvas=Assert.IsType<Canvas>(root.Children[0]);
                        Assert.InRange(canvas.Children.Count,1,13);
                        Assert.Equal("Game 0",carousel.DisplayedTitle);
                        foreach(var hit in canvas.Children.OfType<Grid>())
                        {
                            var art=Assert.IsType<Grid>(hit.Children[0]);
                            Assert.Null(art.Effect);
                            Assert.False(Assert.IsType<AsyncImage>(art.Children[1]).Desaturate);
                        }
                        carousel.StyleMode="FixedAnchor";
                        Assert.Equal("Game 0",carousel.DisplayedTitle);
                        var effects=canvas.Children.OfType<Grid>().Select(h => ((Grid)h.Children[0]).Effect).OfType<System.Windows.Media.Effects.DropShadowEffect>().ToArray();
                        Assert.Single(effects);
                        Assert.Equal(System.Windows.Media.Colors.White,effects[0].Color);
                        carousel.StyleMode="Loop";
                        carousel.Games=new[] { games[0] };
                        Assert.True(canvas.Width >= 3 * (GameCarousel.LoopTileSize + GameCarousel.LoopGap));
                        Assert.Equal("Game 0",carousel.DisplayedTitle);
                        completed.TrySetResult(true);
                    }
                    catch(Exception ex) { completed.TrySetException(ex); }
                    finally { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                };
                timer.Start();
                Dispatcher.Run();
            }
            catch(Exception ex) { timer.Stop(); completed.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground=true; thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
