using COMTool.Sftp;
using COMTool.Sftp.Testing;

namespace COMTool.Sftp.Tests;

public sealed class InMemoryFileSystemTests
{
    [Fact]
    public async Task ListsEntriesWithTheirLogicalPaths()
    {
        var fs = new InMemoryFileSystem("/home/test");
        fs.AddFile("/home/test/a.txt", "hello");
        fs.AddDirectory("/home/test/sub");
        var entries = await fs.ListAsync("/home/test", CancellationToken.None);
        Assert.Equal(2, entries.Count);
        Assert.Equal("/home/test/a.txt", entries.Single(e => e.Name == "a.txt").Path);
        Assert.Equal("/home/test/sub", entries.Single(e => e.Name == "sub").Path);
        Assert.True(entries.Single(e => e.Name == "sub").IsDirectory);
    }

    [Fact]
    public async Task FollowsSymlinkedDirectoriesButKeepsTheRequestedPath()
    {
        var fs = new InMemoryFileSystem("/home/test");
        fs.AddDirectory("/home/test/real");
        fs.AddFile("/home/test/real/inner.txt", "inner");
        fs.AddSymlink("/home/test/link", "/home/test/real");

        var entries = await fs.ListAsync("/home/test/link", CancellationToken.None);
        Assert.Single(entries);
        Assert.Equal("inner.txt", entries[0].Name);

        // The path is rebuilt from the requested path, not from the resolved
        // target path.
        Assert.Equal("/home/test/link/inner.txt", entries[0].Path);
    }

    [Fact]
    public async Task DeletesDirectoriesRecursively()
    {
        var fs = new InMemoryFileSystem("/home/test");
        fs.AddFile("/home/test/dir/a.txt", "a");
        fs.AddFile("/home/test/dir/sub/b.txt", "b");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fs.DeleteDirectoryAsync("/home/test/dir", recursive: false, CancellationToken.None));
        Assert.True(fs.HasEntry("/home/test/dir"));

        await fs.DeleteDirectoryAsync("/home/test/dir", recursive: true, CancellationToken.None);
        Assert.False(fs.HasEntry("/home/test/dir"));
        Assert.False(fs.HasEntry("/home/test/dir/a.txt"));
        Assert.False(fs.HasEntry("/home/test/dir/sub/b.txt"));
    }
}
