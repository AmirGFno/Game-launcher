using Xunit;
using System.IO;
using GameLauncher.Services;

namespace GFnoLauncher.Tests;

public sealed class PathSafetyTests
{
    [Fact]
    public void IsWithin_requires_a_separator_boundary()
    {
        Assert.True(PathSafety.IsWithin(@"C:\root\child", @"C:\root"));
        Assert.True(PathSafety.IsWithin(@"c:\root\child", @"C:\ROOT"));
        Assert.False(PathSafety.IsWithin(@"C:\root2\child", @"C:\root"));
        Assert.False(PathSafety.IsWithin(@"C:\root", @"C:\root"));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    public void IsDriveRoot_detectsRoots(string path)
    {
        Assert.True(PathSafety.IsDriveRoot(path));
        Assert.False(PathSafety.IsDriveRoot(Path.Combine(path, "child")));
    }

    [Fact]
    public void IsSafeToDelete_rejectsPathsOutsideTheRoot()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "safe-root");
        string outside = Path.Combine(AppContext.BaseDirectory, "outside");

        Assert.False(PathSafety.IsSafeToDelete(outside, root));
        Assert.False(PathSafety.IsSafeToDelete(Path.Combine(root, "..", "outside"), root));
        Assert.False(PathSafety.IsSafeToDelete(root, root));
    }

    [Fact]
    public void IsSafeToDelete_acceptsAChildOfTheRoot()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "safe-root");
        string child = Path.Combine(root, "child.tmp");

        Assert.True(PathSafety.IsSafeToDelete(child, root));
    }

    [Fact]
    public void EnsureSafeRoot_rejectsDriveRootsAndProtectedFolders()
    {
        Assert.Throws<ArgumentException>(() => PathSafety.EnsureSafeRoot(@"C:\"));

        string? windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            Assert.Throws<ArgumentException>(() => PathSafety.EnsureSafeRoot(windows));
        }

        Assert.Throws<ArgumentException>(() => PathSafety.EnsureSafeRoot(Path.GetTempPath()));
    }
}
