namespace COMTool.Sftp;

/// <summary>
/// The direction of a transfer.
/// </summary>
public enum SftpTransferDirection
{
    /// <summary>
    /// Local to remote.
    /// </summary>
    Upload,

    /// <summary>
    /// Remote to local.
    /// </summary>
    Download,
}

/// <summary>
/// The stage a transfer is in.
/// </summary>
public enum SftpTransferStage
{
    Scanning,
    Transferring,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// The options of one transfer.
/// </summary>
/// <param name="FollowSymbolicLinks">Follow symbolic links instead of skipping them.</param>
/// <param name="MaxDepth">The maximum number of directory levels to walk.</param>
/// <param name="MaxFiles">The maximum number of files to transfer.</param>
/// <param name="ConflictResolver">
/// Asked what to do when a target already exists. When it is null an
/// existing target is skipped.
/// </param>
public sealed record SftpTransferOptions(
    bool FollowSymbolicLinks = true,
    int MaxDepth = 16,
    int MaxFiles = 100_000,
    Func<SftpConflictContext, Task<SftpConflictDecision>>? ConflictResolver = null);

/// <summary>
/// One transfer request.
/// </summary>
/// <param name="Direction">The direction of the transfer.</param>
/// <param name="SourcePaths">The paths to copy.</param>
/// <param name="TargetDirectory">The directory the files are copied into.</param>
/// <param name="Options">The options of the transfer.</param>
public sealed record SftpTransferRequest(
    SftpTransferDirection Direction,
    IReadOnlyList<string> SourcePaths,
    string TargetDirectory,
    SftpTransferOptions Options);

/// <summary>
/// One progress report of a transfer.
/// </summary>
/// <param name="Stage">The stage the transfer is in.</param>
/// <param name="FileName">The name of the file being copied.</param>
/// <param name="CompletedFiles">The number of files already copied.</param>
/// <param name="TotalFiles">The number of files in the transfer.</param>
/// <param name="CompletedBytes">The number of bytes already copied.</param>
/// <param name="TotalBytes">The number of bytes in the transfer.</param>
public sealed record SftpTransferProgress(
    SftpTransferStage Stage,
    string FileName,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes);

/// <summary>
/// One file that could not be transferred.
/// </summary>
/// <param name="Path">The path that failed.</param>
/// <param name="Reason">Why it failed.</param>
public sealed record SftpTransferFailure(string Path, string Reason);

/// <summary>
/// The outcome of a transfer.
/// </summary>
/// <param name="Succeeded">The number of files that were copied.</param>
/// <param name="Skipped">The number of files that were skipped.</param>
/// <param name="Failed">The number of files that failed.</param>
/// <param name="Cancelled">True when the transfer was cancelled.</param>
/// <param name="BytesTransferred">The number of bytes that were copied.</param>
/// <param name="Failures">The files that failed with their reasons.</param>
/// <param name="SkippedPaths">The paths that were skipped.</param>
public sealed record SftpTransferResult(
    int Succeeded,
    int Skipped,
    int Failed,
    bool Cancelled,
    long BytesTransferred,
    IReadOnlyList<SftpTransferFailure> Failures,
    IReadOnlyList<string> SkippedPaths);
