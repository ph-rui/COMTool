namespace COMTool.Sftp;

/// <summary>
/// The local file system of the machine the application runs on. It is
/// used for the local pane of the SFTP drawer.
/// </summary>
public sealed class SystemLocalFileSystem : ILocalFileSystem
{
    private const int OwnerWriteBit = 0b010_000_000;

    public string HomeDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new List<SftpEntry>();
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            var isDirectory = item is DirectoryInfo;
            var readOnly = item.Attributes.HasFlag(FileAttributes.ReadOnly);

            entries.Add(new SftpEntry(
                item.Name,
                item.FullName,
                isDirectory,
                item.LinkTarget is not null,
                isDirectory ? 0 : ((FileInfo)item).Length,
                item.LastWriteTimeUtc,
                SftpPermission.FromOctalText(readOnly ? "444" : "644")));
        }

        return Task.FromResult<IReadOnlyList<SftpEntry>>(entries);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(oldPath))
        {
            Directory.Move(oldPath, newPath);
        }
        else
        {
            File.Move(oldPath, newPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(path, recursive);
        return Task.CompletedTask;
    }

    public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return Task.CompletedTask;
        }

        var readOnly = (octalMode & OwnerWriteBit) == 0;
        var attributes = File.GetAttributes(path);
        var updated = readOnly
            ? attributes | FileAttributes.ReadOnly
            : attributes & ~FileAttributes.ReadOnly;

        if (updated != attributes)
        {
            File.SetAttributes(path, updated);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
