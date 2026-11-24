using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;
using GameLauncher.Converters;
using GameLauncher.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats.Png;
using Xunit;

namespace GFnoLauncher.Tests;
public sealed class ArtworkPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "gfno-download-" + Guid.NewGuid());
    public ArtworkPipelineTests() => Directory.CreateDirectory(_root);
    private static byte[] ImageBytes(bool webp)
    {
        using var image = new Image<Rgba32>(80,40);
        using var stream = new MemoryStream();
        image.Save(stream, webp ? new WebpEncoder() : new PngEncoder());
        return stream.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadClosesWriterValidatesSniffsFormatAndDecodes(bool webp)
    {
        using var client = new HttpClient(new Handler(request => {
            Assert.Contains("Mozilla/5.0",request.Headers.UserAgent.ToString());
            Assert.Contains("image/webp",request.Headers.Accept.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(ImageBytes(webp)) };
        }));
        string path=await ImageDownloader.DownloadCoreAsync(client,"https://example.test/wrong.jpg",_root,"steam_570_logo",default);
        Assert.EndsWith(webp ? ".webp" : ".png",path);
        Assert.True(File.Exists(path+".downloaded"));
        Assert.Empty(Directory.GetFiles(_root,"*.part"));
        Assert.NotNull(await Task.Run(() => ImageLoader.DecodeManaged(path,32)));
        var bitmap=await ImageLoader.LoadAsync(path,32);
        Assert.NotNull(bitmap); Assert.True(bitmap.IsFrozen); Assert.InRange(bitmap.PixelWidth,1,32);
        // Content detection must still work if a manually added WebP has the wrong suffix.
        string renamed=Path.Combine(_root,"renamed.jpg"); File.Copy(path,renamed);
        Assert.NotNull(await ImageLoader.LoadAsync(renamed,32));
        using var exclusive=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
    }
    [Theory]
    [InlineData(403,false)]
    [InlineData(200,false)]
    [InlineData(200,true)]
    public async Task InvalidResponsesDoNotLeavePartialFiles(int status,bool html)
    {
        using var client=new HttpClient(new Handler(_ => new HttpResponseMessage((HttpStatusCode)status) {
            Content=new ByteArrayContent(html ? System.Text.Encoding.UTF8.GetBytes("<html>This is not an image.</html>") : Array.Empty<byte>()) }));
        await Assert.ThrowsAnyAsync<Exception>(() => ImageDownloader.DownloadCoreAsync(client,"https://example.test/a",_root,null,default));
        Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact]
    public async Task SteamFallsBackAndSkipsValidatedAppIdCache()
    {
        int calls=0;
        string path=Path.Combine(_root,"steam_570_logo.webp");
        async Task<string> Download(string url,string stem,CancellationToken token)
        {
            calls++;
            if(calls<3) throw new HttpRequestException("Unavailable");
            Assert.Contains("shared.cloudflare.steamstatic.com",url);
            Assert.Equal("steam_570_logo",stem);
            await File.WriteAllBytesAsync(path,ImageBytes(true),token);
            return path;
        }
        Assert.Equal(path,await SteamScannerService.GetArtworkAsync(570,false,default,null,Download,_root));
        Assert.Equal(3,calls);
        Assert.Equal(path,await SteamScannerService.GetArtworkAsync(570,false,default,null,Download,_root));
        Assert.Equal(3,calls);
        Assert.Contains(SteamScannerService.ArtworkUrls(570,true),u => u.Contains("shared.cloudflare.steamstatic.com/store_item_assets/"));
    }
    [Fact]
    public async Task AllSteamFailuresReturnNoArtwork()
    {
        int calls=0;
        Task<string> Fail(string _,string __,CancellationToken ___) { calls++; throw new HttpRequestException(); }
        Assert.Null(await SteamScannerService.GetArtworkAsync(999,true,default,null,Fail,_root));
        Assert.Equal(9,calls);
        Assert.Equal(570u,SteamScannerService.AppIdFromTarget("steam://run/570"));
    }
    [Fact]
    public async Task CacheClearKeepsManualArtworkAndLibrary()
    {
        string images=Path.Combine(_root,"Images"); Directory.CreateDirectory(images);
        string manual=Path.Combine(images,"manual.png"), owned=Path.Combine(images,"steam_570_logo.png");
        await File.WriteAllBytesAsync(manual,ImageBytes(false));
        await File.WriteAllBytesAsync(owned,ImageBytes(false));
        await File.WriteAllTextAsync(owned+".downloaded","GFno downloaded artwork");
        await File.WriteAllTextAsync(Path.Combine(_root,"library.json"),"[]");
        var result=await new ArtworkCacheService(_root).ClearAsync();
        Assert.Equal(1,result.Deleted);
        Assert.True(File.Exists(manual)); Assert.True(File.Exists(Path.Combine(_root,"library.json")));
        Assert.False(File.Exists(owned));
    }
    [Fact]
    public async Task SteamHttp403FallsBackToValidated200()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImageBytes(true)) }));
        var path = await SteamScannerService.GetArtworkAsync(570, false, default, null,
            (url, stem, token) => ImageDownloader.DownloadCoreAsync(client, url, _root, stem, token), _root);
        Assert.NotNull(path); Assert.Equal(2, calls);
        ImageDownloader.ValidateImageFile(path!);
        Assert.Empty(Directory.GetFiles(_root, "*.part"));
    }
    public void Dispose() => Directory.Delete(_root,true);
}
