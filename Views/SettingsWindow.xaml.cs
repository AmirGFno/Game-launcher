using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Views;
public partial class SettingsWindow : Window
{
    private readonly LauncherSettingsService _service;
    public LauncherSettings Settings { get; private set; }
    public bool ArtworkCleared { get; private set; }
    public SettingsWindow(LauncherSettings settings, LauncherSettingsService service)
    {
        InitializeComponent(); _service = service; Settings = settings.Clone();
        Loop.IsChecked = settings.CarouselStyle == "Loop"; Anchor.IsChecked = !Loop.IsChecked;
        Fullscreen.IsChecked=settings.Fullscreen; Cleanup.IsChecked=settings.CleanCacheBeforeLaunch; Tray.IsChecked=settings.CloseToTray;
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        Settings.CarouselStyle = Anchor.IsChecked == true ? "FixedAnchor" : "Loop";
        Settings.Fullscreen=Fullscreen.IsChecked==true; Settings.CleanCacheBeforeLaunch=Cleanup.IsChecked==true; Settings.CloseToTray=Tray.IsChecked==true;
        try { _service.Save(Settings); DialogResult=true; }
        catch (Exception ex) { Result.Text="Could not save settings: " + ex.Message; }
    }
    private async void ClearArtwork_Click(object sender,RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this,"Delete tracked artwork downloads? Your library and manually added artwork are kept. Deleted downloads must be downloaded again.","Clear artwork",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        var button=(System.Windows.Controls.Button)sender; button.IsEnabled=false;
        try { var result=await new ArtworkCacheService().ClearAsync(); ArtworkCleared=true; Result.Text=$"Deleted {result.Deleted} files; skipped {result.Skipped}."; }
        catch(Exception ex) { Result.Text=ex.Message; }
        finally { button.IsEnabled=true; }
    }
}
