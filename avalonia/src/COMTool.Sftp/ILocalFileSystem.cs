namespace COMTool.Sftp;

/// <summary>
/// The local side of a file browser. The paths are local paths of the
/// machine the application runs on.
/// </summary>
public interface ILocalFileSystem : IAsyncDisposable
{
    /// <summary>
    /// The directory the browser starts in, normally the user profile.
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
    /// Applies a POSIX mode. Only the owner write bit has a meaning on a
    /// local file system.
    /// </summary>
    Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);
}
