using System.IO;
using GameLauncher.Controls;
using GameLauncher.Models;
using GameLauncher.Services;
using Xunit;

namespace GFnoLauncher.Tests;
public sealed class CarouselSettingsSteamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"gfno-settings-"+Guid.NewGuid());
    public CarouselSettingsSteamTests() => Directory.CreateDirectory(_root);
    [Theory]
    [InlineData(5,0,6,true,1)]
    [InlineData(0,5,6,true,-1)]
    [InlineData(0,4,5,false,1)]
    [InlineData(0,0,6,true,0)]
    public void LoopTakesAdjacentStepAcrossSeam(int from,int to,int count,bool loop,int expected)
        => Assert.Equal(expected,GameCarousel.Direction(from,to,count,loop));
    [Theory]
    [InlineData(-1,6,5)]
    [InlineData(6,6,0)]
    [InlineData(-13,6,5)]
    public void WrapHandlesBothDirections(int index,int count,int expected) => Assert.Equal(expected,GameCarousel.Wrap(index,count));
    [Fact]
    public void SettingsRoundTripAndBackup()
    {
        string path=Path.Combine(_root,"settings.json");
        var service=new LauncherSettingsService(path);
        Assert.False(service.Load().CleanCacheBeforeLaunch);
        var settings=new LauncherSettings { CarouselStyle="FixedAnchor", Fullscreen=true, CleanCacheBeforeLaunch=true, CloseToTray=false };
        service.Save(settings); service.Save(settings);
        var read=new LauncherSettingsService(path).Load();
        Assert.Equal("FixedAnchor",read.CarouselStyle);
        Assert.True(read.Fullscreen); Assert.True(read.CleanCacheBeforeLaunch); Assert.False(read.CloseToTray);
        Assert.True(File.Exists(path+".bak"));
    }
    [Fact]
    public void InvalidSettingsFallBackWithoutDeletingFile()
    {
        string path=Path.Combine(_root,"settings.json"); File.WriteAllText(path,"{broken");
        Assert.Equal("Loop",new LauncherSettingsService(path).Load().CarouselStyle);
        Assert.Equal("{broken",File.ReadAllText(path));
    }
    [Fact]
    public void ReadsInstalledSteamManifestsAndUsesRequestedArtworkNames()
    {
        File.WriteAllText(Path.Combine(_root,"appmanifest_570.acf"),"\"AppState\" { \"appid\" \"570\" \"name\" \"Dota 2\" }");
        File.WriteAllText(Path.Combine(_root,"appmanifest_bad.acf"),"\"appid\" \"bad\"");
        var game=Assert.Single(SteamScannerService.ReadManifests(_root));
        Assert.Equal(570u,game.AppId); Assert.Equal("Dota 2",game.Name);
        Assert.Equal("https://cdn.cloudflare.steamstatic.com/steam/apps/570/library_600x900.jpg",SteamScannerService.LogoUrl(game.AppId));
        Assert.Equal("https://cdn.cloudflare.steamstatic.com/steam/apps/570/library_hero.jpg",SteamScannerService.BackgroundUrl(game.AppId));
    }
    public void Dispose() => Directory.Delete(_root,true);
}
