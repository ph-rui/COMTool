namespace COMTool.Sftp;

/// <summary>
/// What the user decided when a transfer hit an existing target.
/// </summary>
public enum SftpConflictAction
{
    /// <summary>
    /// Replace the target with the source.
    /// </summary>
    Overwrite,

    /// <summary>
    /// Leave the target alone and continue with the next file.
    /// </summary>
    Skip,

    /// <summary>
    /// Write to a different name.
    /// </summary>
    Rename,

    /// <summary>
    /// Abort the whole transfer.
    /// </summary>
    Cancel,
}

/// <summary>
/// The decision for one conflict.
/// </summary>
/// <param name="Action">What to do with the target.</param>
/// <param name="NewName">
/// The new name for a rename. When it is null the suggested name is used.
/// </param>
public sealed record SftpConflictDecision(SftpConflictAction Action, string? NewName = null);

/// <summary>
/// The information about one conflict that is handed to the user.
/// </summary>
/// <param name="SourcePath">The path the file is copied from.</param>
/// <param name="TargetPath">The path that already exists.</param>
/// <param name="TargetSize">The size of the existing target in bytes.</param>
/// <param name="TargetLastWriteUtc">The last write time of the existing target.</param>
/// <param name="SuggestedRename">A free name the target could be renamed to.</param>
public sealed record SftpConflictContext(
    string SourcePath,
    string TargetPath,
    long TargetSize,
    DateTime TargetLastWriteUtc,
    string SuggestedRename);

/// <summary>
/// Builds the names offered when a transfer hits an existing target.
/// </summary>
public static class SftpConflictNaming
{
    /// <summary>
    /// Inserts the index before the extension, so "a.txt" becomes
    /// "a (1).txt" and "a" becomes "a (1)".
    /// </summary>
    public static string Suggest(string fileName, int index)
    {
        var name = fileName ?? string.Empty;
        var dot = name.LastIndexOf('.');
        if (dot <= 0)
        {
            return $"{name} ({index})";
        }

        var stem = name[..dot];
        var extension = name[dot..];
        return $"{stem} ({index}){extension}";
    }
}
