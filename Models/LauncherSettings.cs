namespace GameLauncher.Models;

public sealed class LauncherSettings
{
    public string CarouselStyle { get; set; } = "Loop";
    public bool Fullscreen { get; set; }
    public bool CleanCacheBeforeLaunch { get; set; }
    public bool MinimizeToTrayOnLaunch { get; set; } // Default: minimize to taskbar, never quit.
    public bool CloseToTray { get; set; } = true;
    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();
}
