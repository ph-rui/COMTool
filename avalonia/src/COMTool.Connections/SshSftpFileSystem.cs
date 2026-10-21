using System.Reflection;
using System.Runtime.ExceptionServices;
using COMTool.Sftp;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace COMTool.Connections;

/// <summary>
/// The remote side of the SFTP drawer, implemented over SSH.NET's
/// <see cref="SftpClient"/>.
/// </summary>
/// <remarks>
/// SSH.NET cannot share the channel of an open shell with the sftp
/// subsystem, so this type opens its own connection with the same
/// credentials.
/// </remarks>
public sealed class SshSftpFileSystem : ISftpFileSystem
{
    private readonly SftpClient _client;
    private int _disposed;

    public SshSftpFileSystem(ConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        _client = new SftpClient(connectionInfo);
        try
        {
            _client.Connect();
        }
        catch
        {
            _client.Dispose();
            throw;
        }

        HomeDirectory = _client.WorkingDirectory;
    }

    public string HomeDirectory { get; }

    public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var requested = SftpPath.Normalize(path);
        var entries = new List<SftpEntry>();

        try
        {
            foreach (var file in ListEntries(requested))
            {
                entries.Add(new SftpEntry(
                    file.Name,
                    SftpPath.Combine(requested, file.Name),
                    file.IsDirectory,
                    file.IsSymbolicLink,
                    file.Length,
                    file.LastWriteTimeUtc,
                    new SftpPermission(
                        file.OwnerCanRead,
                        file.OwnerCanWrite,
                        file.OwnerCanExecute,
                        file.GroupCanRead,
                        file.GroupCanWrite,
                        file.GroupCanExecute,
                        file.OthersCanRead,
                        file.OthersCanWrite,
                        file.OthersCanExecute)));
            }
        }
        catch (SftpException ex)
        {
            // The server answers with "no such file" when the path is a
            // file and not a directory. The transfer engine asks with a
            // listing whether an entry is a directory, so the answer has
            // to look like a path that is not there.
            throw new DirectoryNotFoundException(ex.Message, ex);
        }

        return Task.FromResult<IReadOnlyList<SftpEntry>>(entries);
    }

    /// <summary>
    /// Lists the content of a directory. The server sends the two entries
    /// of the directory itself, they are not part of the content and are
    /// left out.
    /// </summary>
    private List<ISftpFile> ListEntries(string path)
    {
        var entries = new List<ISftpFile>();
        foreach (var file in _client.ListDirectory(path))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            entries.Add(file);
        }

        return entries;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(_client.Open(path, FileMode.Open, FileAccess.Read));
    }

    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // FileMode.Create truncates a target that is already there.
        return Task.FromResult<Stream>(_client.Open(path, FileMode.Create, FileAccess.Write));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _client.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (IsSymbolicLink(oldPath))
        {
            // A link is renamed itself, not the file it points at.
            RequestRenameRaw(oldPath, newPath);
            return Task.CompletedTask;
        }

        _client.RenameFile(oldPath, newPath);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (IsSymbolicLink(path))
        {
            // A link is removed itself, not the file it points at.
            RequestRemoveRaw(path);
            return Task.CompletedTask;
        }

        _client.DeleteFile(path);
        return Task.CompletedTask;
    }

    public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!recursive)
        {
            _client.DeleteDirectory(path);
            return;
        }

        // The server refuses to remove a directory that is not empty, so
        // the tree is emptied first.
        await EmptyDirectoryAsync(path, cancellationToken);
        _client.DeleteDirectory(path);
    }

    public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // The SFTP protocol carries the mode as the decimal number whose
        // digits are the octal digits, so the mode 0644 goes over the wire
        // as 644 and not as 420.
        var digits = SftpPermission.FromOctal(octalMode).ToOctalText();
        var wire = short.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        _client.ChangePermissions(path, wire);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_client.Exists(path));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Removes everything below a directory, so that the server accepts the
    /// remove of the directory itself.
    /// </summary>
    private async Task EmptyDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var file in ListEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var child = SftpPath.Combine(path, file.Name);

            if (file.IsSymbolicLink)
            {
                // A link is removed, not the file it points at.
                RequestRemoveRaw(child);
                continue;
            }

            if (file.IsDirectory)
            {
                await EmptyDirectoryAsync(child, cancellationToken);
                _client.DeleteDirectory(child);
                continue;
            }

            _client.DeleteFile(child);
        }
    }

    /// <summary>
    /// Returns whether a path is a symbolic link. The listing of the
    /// parent directory holds the answer.
    /// </summary>
    private bool IsSymbolicLink(string path)
    {
        var name = SftpPath.GetFileName(path);
        if (name.Length == 0)
        {
            return false;
        }

        try
        {
            foreach (var file in ListEntries(SftpPath.GetParent(path)))
            {
                if (file.Name == name)
                {
                    return file.IsSymbolicLink;
                }
            }
        }
        catch (SftpException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Sends the remove request of the protocol without resolving the
    /// path.
    /// </summary>
    /// <remarks>
    /// The library resolves a symbolic link to its target before it sends
    /// the remove, so the public API would delete the target of the link
    /// and leave the link behind. The request of the protocol removes the
    /// link itself. When the library does not offer the request, the
    /// caller gets a clear error rather than a deleted target.
    /// </remarks>
    private void RequestRemoveRaw(string path)
    {
        InvokeSessionRequest("RequestRemove", path);
    }

    /// <summary>
    /// Sends the rename request of the protocol without resolving the
    /// path. A link is renamed itself, not the file it points at.
    /// </summary>
    private void RequestRenameRaw(string oldPath, string newPath)
    {
        InvokeSessionRequest("RequestRename", oldPath, newPath);
    }

    private void InvokeSessionRequest(string name, params object[] arguments)
    {
        var session = GetSftpSession();
        var method = session?.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (session is null || method is null)
        {
            throw new NotSupportedException(
                $"The library does not offer the request '{name}', so a symbolic link cannot be changed without touching its target.");
        }

        try
        {
            method.Invoke(session, arguments);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
        }
    }

    /// <summary>
    /// The session of the sftp client. The library keeps it for itself.
    /// </summary>
    private object? GetSftpSession()
    {
        var property = typeof(SftpClient).GetProperty(
            "SftpSession",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return property?.GetValue(_client);
    }

    private void EnsureNotDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SshSftpFileSystem));
        }
    }
}
