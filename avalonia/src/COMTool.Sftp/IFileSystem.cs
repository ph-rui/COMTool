namespace COMTool.Sftp;

/// <summary>
/// The operations both sides of the SFTP drawer share. The remote side and
/// the local side are separate interfaces so that a test double can
/// implement either one on its own, but they derive from this one so that
/// the transfer engine can walk both with the same code.
/// </summary>
public interface IFileSystem : IAsyncDisposable
{
    /// <summary>
    /// The directory the browser starts in.
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
