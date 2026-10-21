using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class SystemLocalFileSystemTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "comtool-sftp-local-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task ListsFilesAndDirectoriesWithTheirSize()
    {
        var root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            var fs = new SystemLocalFileSystem();
            var entries = await fs.ListAsync(root, CancellationToken.None);
            var file = entries.Single(entry => entry.Name == "a.txt");
            Assert.False(file.IsDirectory);
            Assert.Equal(5, file.Size);
            Assert.Equal(Path.Combine(root, "a.txt"), file.Path);
            Assert.True(entries.Single(entry => entry.Name == "sub").IsDirectory);
            Assert.False(string.IsNullOrEmpty(fs.HomeDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatesRenamesAndDeletes()
    {
        var root = NewRoot();
        try
        {
            var fs = new SystemLocalFileSystem();
            var sub = Path.Combine(root, "sub");
            var renamed = Path.Combine(root, "renamed");

            await fs.CreateDirectoryAsync(sub, CancellationToken.None);
            Assert.True(await fs.ExistsAsync(sub, CancellationToken.None));
            Assert.True(Directory.Exists(sub));

            // An existing directory is reused, not an error.
            await fs.CreateDirectoryAsync(sub, CancellationToken.None);
            Assert.True(await fs.ExistsAsync(sub, CancellationToken.None));

            await fs.RenameAsync(sub, renamed, CancellationToken.None);
            Assert.False(await fs.ExistsAsync(sub, CancellationToken.None));
            Assert.True(await fs.ExistsAsync(renamed, CancellationToken.None));

            var file = Path.Combine(root, "b.txt");
            await File.WriteAllTextAsync(file, "x");
            await fs.DeleteFileAsync(file, CancellationToken.None);
            Assert.False(await fs.ExistsAsync(file, CancellationToken.None));

            // Deleting a missing file is not an error.
            await fs.DeleteFileAsync(file, CancellationToken.None);

            await fs.DeleteDirectoryAsync(renamed, recursive: true, CancellationToken.None);
            Assert.False(await fs.ExistsAsync(renamed, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RoundTripsFileContent()
    {
        var root = NewRoot();
        try
        {
            var fs = new SystemLocalFileSystem();
            var file = Path.Combine(root, "a.txt");

            await using (var write = await fs.OpenWriteAsync(file, CancellationToken.None))
            {
                await write.WriteAsync("hello"u8.ToArray());
            }

            await using (var read = await fs.OpenReadAsync(file, CancellationToken.None))
            using (var reader = new StreamReader(read))
            {
                Assert.Equal("hello", await reader.ReadToEndAsync());
            }

            // Opening for write truncates the existing file.
            await using (var write = await fs.OpenWriteAsync(file, CancellationToken.None))
            {
                await write.WriteAsync("hi"u8.ToArray());
            }

            await using (var read = await fs.OpenReadAsync(file, CancellationToken.None))
            using (var reader = new StreamReader(read))
            {
                Assert.Equal("hi", await reader.ReadToEndAsync());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PermissionMapsToTheReadOnlyAttribute()
    {
        var root = NewRoot();
        try
        {
            var fs = new SystemLocalFileSystem();
            var file = Path.Combine(root, "a.txt");
            await File.WriteAllTextAsync(file, "hello");

            await fs.SetPermissionsAsync(
                file,
                SftpPermission.FromOctalText("444").ToOctal(),
                CancellationToken.None);
            Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));

            await fs.SetPermissionsAsync(
                file,
                SftpPermission.FromOctalText("644").ToOctal(),
                CancellationToken.None);
            Assert.False(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
