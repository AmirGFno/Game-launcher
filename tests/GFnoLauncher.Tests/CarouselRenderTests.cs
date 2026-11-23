using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            try
            {
                var games = new ObservableCollection<GameItem>(Enumerable.Range(0,6).Select(i => new GameItem { Title="Game " + i }));
                var carousel = new GameCarousel { Width=1200, Height=290, Games=games, SelectedGame=games[5] };
                carousel.Measure(new Size(1200,290));
                carousel.Arrange(new Rect(0,0,1200,290));
                carousel.SelectedGame=games[0];
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        Assert.Same(games[0],carousel.SelectedGame);
                        var canvas=Assert.IsType<Canvas>(carousel.Content);
                        Assert.InRange(canvas.Children.Count,1,13);
                        Assert.Contains(canvas.Children.OfType<Border>(),b => b.Child is TextBlock title && title.Text=="Game 0");
                        Assert.All(canvas.Children.OfType<Grid>().SelectMany(tile => tile.Children.OfType<AsyncImage>()),image => Assert.False(image.Desaturate));
                        completed.TrySetResult(true);
                    }
                    catch(Exception ex) { completed.TrySetException(ex); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
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
