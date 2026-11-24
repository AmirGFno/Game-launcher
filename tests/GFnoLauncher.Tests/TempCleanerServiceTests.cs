using Xunit;
using System.IO;
using GameLauncher.Services;

namespace GFnoLauncher.Tests;

public sealed class TempCleanerServiceTests : IDisposable
{
    private readonly string _root;

    public TempCleanerServiceTests()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "gfno-clean-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static Progress<string> Silent() => new(_ => { });

    [Fact]
    public async Task Preview_listsEntriesWithoutDeletingAnything()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "b");

        var service = new TempCleanerService(_root);
        CleanupPreview preview = await service.PreviewAsync();

        Assert.Equal(2, preview.EntryCount);
        Assert.True(File.Exists(Path.Combine(_root, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "sub", "b.txt")));
    }

    [Fact]
    public async Task Clean_removesTheEntriesAndReportsThem()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "b");

        var service = new TempCleanerService(_root);
        CleanupResult result = await service.CleanAsync(Silent(), CancellationToken.None);

        Assert.Equal(2, result.Deleted);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task Clean_isCancellable()
    {
        for (int i = 0; i < 20; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"file-{i}.txt"), "x");
        }

        var service = new TempCleanerService(_root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CleanAsync(Silent(), cancellation.Token));
    }

    [Fact]
    public void Constructor_rejectsUnsafeRoots()
    {
        Assert.Throws<ArgumentException>(() => new TempCleanerService(@"C:\"));
        Assert.Throws<ArgumentException>(() => new TempCleanerService(Path.GetTempPath()));
    }

    [Fact]
    public async Task LockedNestedFileDoesNotPreventCleaningItsSiblings()
    {
        string folder = Path.Combine(_root, "nested"); Directory.CreateDirectory(folder);
        string locked = Path.Combine(folder, "locked.txt"), removable = Path.Combine(folder, "remove.txt");
        File.WriteAllText(locked, "keep"); File.WriteAllText(removable, "delete");
        using var handle = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await new TempCleanerService(_root).CleanAsync(Silent());
        Assert.True(File.Exists(locked)); Assert.False(File.Exists(removable));
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Clean_doesNotFollowJunctions()
    {
        string target = Path.Combine(AppContext.BaseDirectory, "gfno-junction-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        string parent = Path.Combine(_root, "nested");
        Directory.CreateDirectory(parent);
        string link = Path.Combine(parent, "linked");

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c mklink /J \"{link}\" \"{target}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                // Creating junctions is not always permitted - nothing to assert.
                return;
            }

            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
            var service = new TempCleanerService(_root);
            await service.CleanAsync(Silent(), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(target, "keep.txt")),
                "the junction target must never be touched");
        }
        finally
        {
            try
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
