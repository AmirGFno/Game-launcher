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
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            try
            {
                var games = new ObservableCollection<GameItem>(Enumerable.Range(0,6).Select(i => new GameItem { Title="Game " + i }));
                var carousel = new GameCarousel { Width=1200, Height=290, Games=games, SelectedGame=games[5] };
                var window = new Window { Width=1200, Height=350, ShowInTaskbar=false, WindowStyle=WindowStyle.None,
                    Left=-10000, Top=-10000, Content=carousel };
                window.Show();
                window.UpdateLayout();
                dispatcher.BeginInvoke(new Action(() => carousel.SelectedGame=games[0]),DispatcherPriority.Loaded);
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        Assert.Same(games[0],carousel.SelectedGame);
                        var canvas=Assert.IsType<Canvas>(carousel.Content);
                        Assert.InRange(canvas.Children.Count,1,13);
                        var titles=canvas.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text).ToArray();
                        Assert.True(titles.Contains("Game 0"), "Rendered titles after wrap: " + string.Join(",",titles));
                        Assert.All(canvas.Children.OfType<Grid>().SelectMany(tile => tile.Children.OfType<AsyncImage>()),image => Assert.False(image.Desaturate));
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
