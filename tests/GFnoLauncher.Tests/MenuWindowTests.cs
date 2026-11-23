using System.Windows;
using System.Windows.Controls;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.ViewModels;
using GameLauncher.Views;
using Xunit;

namespace GFnoLauncher.Tests;
public class MenuWindowTests
{
    [Fact]
    public async Task BothMenusLoadTheirOwnResourcesAndKeepCapabilities()
    {
        var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(() => {
            try
            {
                var settings=new SettingsWindow(new LauncherSettings(),new LauncherSettingsService());
                settings.Measure(new Size(620,760));
                Assert.Equal("Loop Style",((RadioButton)settings.FindName("Loop")).Content);
                Assert.NotNull(settings.FindName("Fullscreen")); Assert.NotNull(settings.FindName("Cleanup")); Assert.NotNull(settings.FindName("Tray"));
                var editor=new GameEditorWindow(new GameEditorViewModel(new LaunchFlowTests.Dialogs()));
                editor.Measure(new Size(860,800));
                Assert.NotNull(editor.FindResource("MenuField"));
                Assert.NotNull(editor.FindResource("MenuPrimary"));
                settings.Close(); editor.Close(); done.TrySetResult(true);
            }
            catch(Exception ex) { done.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground=true; thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
