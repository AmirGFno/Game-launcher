using Xunit;
using GameLauncher.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace GFnoLauncher.Tests;

public sealed class ImageDownloaderValidationTests : IDisposable
{
    private readonly string _directory;

    public ImageDownloaderValidationTests()
    {
        _directory = Path.Combine(AppContext.BaseDirectory, "gfno-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void RejectsFilesThatAreNotImages()
    {
        string path = Path.Combine(_directory, "fake.png");
        File.WriteAllText(path, "this is definitely not an image");

        Assert.Throws<InvalidOperationException>(() => ImageDownloader.ValidateImageFile(path));
    }

    [Fact]
    public void RejectsEmptyFiles()
    {
        string path = Path.Combine(_directory, "empty.png");
        File.WriteAllText(path, string.Empty);

        Assert.Throws<InvalidOperationException>(() => ImageDownloader.ValidateImageFile(path));
    }

    [Fact]
    public void AcceptsRealImages()
    {
        string path = Path.Combine(_directory, "real.png");

        using (var image = new Image<Rgba32>(8, 6))
        {
            image.Save(path, new PngEncoder());
        }

        ImageDownloader.ValidateImageFile(path);
    }
}
