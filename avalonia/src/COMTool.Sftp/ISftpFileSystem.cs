namespace COMTool.Sftp;

/// <summary>
/// The remote side of a file browser. The paths are remote POSIX paths and
/// always use '/'.
/// </summary>
public interface ISftpFileSystem : IAsyncDisposable
{
    /// <summary>
    /// The home directory the session starts in.
    /// </summary>
    string HomeDirectory { get; }

    Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the file for writing and truncates it when it already exists.
    /// </summary>
    Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a POSIX mode, for example 0644 written as 420.
    /// </summary>
    Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);
}
