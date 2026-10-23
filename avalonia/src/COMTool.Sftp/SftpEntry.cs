namespace COMTool.Sftp;

/// <summary>
/// One entry of a directory listing.
/// </summary>
/// <param name="Name">The entry name without any path.</param>
/// <param name="Path">The full path of the entry.</param>
/// <param name="IsDirectory">True when the entry is a directory.</param>
/// <param name="IsSymbolicLink">True when the entry is a symbolic link.</param>
/// <param name="Size">The size in bytes, zero for a directory.</param>
/// <param name="LastWriteTimeUtc">The last write time in UTC.</param>
/// <param name="Permission">The nine permission bits.</param>
/// <param name="Owner">
/// The owner of the entry when the file system can tell it. The remote
/// side resolves the uid through <c>/etc/passwd</c> and falls back to the
/// number.
/// </param>
public sealed record SftpEntry(
    string Name,
    string Path,
    bool IsDirectory,
    bool IsSymbolicLink,
    long Size,
    DateTime LastWriteTimeUtc,
    SftpPermission Permission,
    string Owner = "");
