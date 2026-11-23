using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.ViewModels;
using Xunit;

namespace GFnoLauncher.Tests;
public class LaunchFlowTests
{
    [Theory]
    [InlineData(true,false)]
    [InlineData(true,true)]
    [InlineData(false,false)]
    public async Task LaunchPreparesFullscreenBeforeWorkerAndRestoresBeforeError(bool fullscreen,bool fail)
    {
        var events=new List<string>();
        var dialogs=new Dialogs { Error=() => { lock(events) events.Add("error"); } };
        var launcher=new Launcher { Start=() => { lock(events) events.Add("launch"); if(fail) throw new InvalidOperationException("Missing exe"); } };
        var library=new Library();
        var vm=new MainViewModel(library,new Cleaner(),launcher,dialogs) { Settings=new LauncherSettings { Fullscreen=fullscreen } };
        vm.SelectedGame=new GameItem { Title="Test",LaunchTarget="test.exe" };
        vm.PrepareFullscreenLaunchAsync=_ => { lock(events) events.Add("prepare"); return Task.CompletedTask; };
        vm.FullscreenLaunchFinished+=(_,success) => { lock(events) events.Add(success ? "success" : "restore"); };
        vm.PlayCommand.Execute(null);
        for(int i=0;i<300 && !vm.PlayCommand.CanExecute(null);i++) await Task.Delay(10);
        Assert.True(vm.PlayCommand.CanExecute(null));
        string[] actual; lock(events) actual=events.ToArray();
        Assert.Equal(fullscreen ? (fail ? new[]{"prepare","launch","restore","error"} : new[]{"prepare","launch","success"}) : new[]{"launch"},actual);
        Assert.Equal(fail ? 0 : 1,vm.SelectedGame.LaunchCount);
    }
    internal sealed class Library : IGameLibraryService
    {
        public Task<IReadOnlyList<GameItem>> LoadAsync() => Task.FromResult<IReadOnlyList<GameItem>>(Array.Empty<GameItem>());
        public Task SaveAsync(IReadOnlyCollection<GameItem> games) => Task.CompletedTask;
    }
    internal sealed class Cleaner : ITempCleanerService
    {
        public string CacheDirectory=>"unused";
        public Task<CleanupPreview> PreviewAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new CleanupPreview(0,0));
        public Task<CleanupResult> CleanAsync(IProgress<string> progress,CancellationToken cancellationToken=default)=>Task.FromResult(new CleanupResult(0,0,0));
    }
    internal sealed class Launcher : IGameLauncherService { public Action Start=()=>{}; public void Launch(GameItem game)=>Start(); }
    internal sealed class Dialogs : IDialogService
    {
        public Action Error=()=>{};
        public string? OpenFile(string filter)=>null;
        public string? OpenFolder()=>null;
        public void ShowError(string title,string message)=>Error();
        public void ShowInfo(string title,string message) { }
        public bool Confirm(string title,string message)=>false;
        public GameItem? ShowGameEditor(GameEditorViewModel vm)=>null;
    }
}
