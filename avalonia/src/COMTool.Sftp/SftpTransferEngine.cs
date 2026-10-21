namespace COMTool.Sftp;

/// <summary>
/// Copies files between the remote side and the local side of the SFTP
/// drawer.
/// </summary>
/// <remarks>
/// The engine works in two phases. The scan phase walks the source side and
/// collects the files to copy, the directories to create and the entries
/// that are skipped. The transfer phase copies the files one by one and
/// asks the conflict resolver what to do when a target already exists.
/// </remarks>
public sealed class SftpTransferEngine
{
    private const int BufferSize = 64 * 1024;
    private const int MaxRenameSuggestions = 1000;

    private static readonly char[] PathSeparators = { '/', '\\' };

    private readonly ISftpFileSystem _remote;
    private readonly ILocalFileSystem _local;

    public SftpTransferEngine(ISftpFileSystem remote, ILocalFileSystem local)
    {
        _remote = remote;
        _local = local;
    }

    public async Task<SftpTransferResult> RunAsync(
        SftpTransferRequest request,
        IProgress<SftpTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var source = request.Direction == SftpTransferDirection.Upload
            ? (IFileSystem)_local
            : (IFileSystem)_remote;
        var target = request.Direction == SftpTransferDirection.Upload
            ? (IFileSystem)_remote
            : (IFileSystem)_local;

        var scan = new ScanResult();
        var failures = new List<SftpTransferFailure>();
        var succeeded = 0;
        var completedBytes = 0L;
        var totalFiles = 0;
        var totalBytes = 0L;
        var cancelled = false;

        try
        {
            await ScanAsync(request, source, scan, progress, cancellationToken);

            totalFiles = scan.Files.Count;
            totalBytes = scan.Files.Sum(file => file.Size);

            await CreateDirectoriesAsync(target, scan.Directories, cancellationToken);

            foreach (var file in scan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = file.TargetPath;

                if (await target.ExistsAsync(targetPath, cancellationToken))
                {
                    var decision = await ResolveConflictAsync(
                        request.Options,
                        target,
                        file,
                        scan,
                        cancellationToken);

                    if (decision.Action == SftpConflictAction.Skip)
                    {
                        continue;
                    }

                    if (decision.Action == SftpConflictAction.Cancel)
                    {
                        cancelled = true;
                        break;
                    }

                    if (decision.Action == SftpConflictAction.Rename)
                    {
                        targetPath = CombineTarget(ParentOf(targetPath), decision.NewName ?? file.FileName);
                    }
                }

                try
                {
                    completedBytes += await CopyAsync(
                        source,
                        target,
                        file,
                        targetPath,
                        succeeded,
                        totalFiles,
                        totalBytes,
                        completedBytes,
                        progress,
                        cancellationToken);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    // A half written file is removed, so that a later run
                    // does not see a broken target.
                    await TryDeleteAsync(target, targetPath);
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    failures.Add(new SftpTransferFailure(file.SourcePath, ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        var stage = cancelled
            ? SftpTransferStage.Cancelled
            : failures.Count > 0
                ? SftpTransferStage.Failed
                : SftpTransferStage.Completed;

        progress?.Report(new SftpTransferProgress(
            stage,
            string.Empty,
            succeeded,
            totalFiles,
            completedBytes,
            totalBytes));

        return new SftpTransferResult(
            succeeded,
            scan.SkippedPaths.Count,
            failures.Count,
            cancelled,
            completedBytes,
            failures,
            scan.SkippedPaths);
    }

    private static async Task ScanAsync(
        SftpTransferRequest request,
        IFileSystem source,
        ScanResult scan,
        IProgress<SftpTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SftpTransferProgress(
            SftpTransferStage.Scanning,
            string.Empty,
            0,
            0,
            0,
            0));

        foreach (var sourcePath in request.SourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = FileNameOf(sourcePath);
            if (name.Length == 0)
            {
                // The root has no name of its own.
                continue;
            }

            var targetPath = CombineTarget(request.TargetDirectory, name);

            if (!request.Options.FollowSymbolicLinks
                && await IsSymbolicLinkAsync(source, sourcePath, cancellationToken))
            {
                scan.SkippedPaths.Add(sourcePath);
                continue;
            }

            if (await IsDirectoryAsync(source, sourcePath, cancellationToken))
            {
                scan.Directories.Add(targetPath);
                await ScanDirectoryAsync(
                    request,
                    source,
                    sourcePath,
                    targetPath,
                    1,
                    scan,
                    cancellationToken);
                continue;
            }

            if (scan.Files.Count >= request.Options.MaxFiles)
            {
                scan.SkippedPaths.Add(sourcePath);
                continue;
            }

            var size = await SizeOfAsync(source, sourcePath, cancellationToken);
            scan.Files.Add(new TransferItem(sourcePath, targetPath, name, size));
        }

        var totalBytes = scan.Files.Sum(file => file.Size);
        progress?.Report(new SftpTransferProgress(
            SftpTransferStage.Scanning,
            string.Empty,
            scan.Files.Count,
            scan.Files.Count,
            totalBytes,
            totalBytes));
    }

    private static async Task ScanDirectoryAsync(
        SftpTransferRequest request,
        IFileSystem source,
        string sourceDirectory,
        string targetDirectory,
        int depth,
        ScanResult scan,
        CancellationToken cancellationToken)
    {
        var entries = await source.ListAsync(sourceDirectory, cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSelfReference(entry.Name))
            {
                continue;
            }

            if (entry.IsSymbolicLink && !request.Options.FollowSymbolicLinks)
            {
                scan.SkippedPaths.Add(entry.Path);
                continue;
            }

            var childTarget = CombineTarget(targetDirectory, entry.Name);

            var isDirectory = entry.IsDirectory;
            if (entry.IsSymbolicLink)
            {
                // A link is followed, so it is a directory when it can be
                // listed and a file otherwise.
                isDirectory = await IsDirectoryAsync(source, entry.Path, cancellationToken);
            }

            if (isDirectory)
            {
                if (depth + 1 > request.Options.MaxDepth)
                {
                    // The branch is too deep. The files below it are
                    // reported as skipped, so that the user knows what was
                    // left behind.
                    await CollectSkippedAsync(
                        request,
                        source,
                        entry.Path,
                        scan,
                        cancellationToken);
                    continue;
                }

                scan.Directories.Add(childTarget);
                await ScanDirectoryAsync(
                    request,
                    source,
                    entry.Path,
                    childTarget,
                    depth + 1,
                    scan,
                    cancellationToken);
                continue;
            }

            if (scan.Files.Count >= request.Options.MaxFiles)
            {
                scan.SkippedPaths.Add(entry.Path);
                continue;
            }

            scan.Files.Add(new TransferItem(entry.Path, childTarget, entry.Name, entry.Size));
        }
    }

    /// <summary>
    /// The two entries of the directory itself are not part of its content.
    /// A server may send them, the way the sftp server of OpenSSH does.
    /// </summary>
    private static bool IsSelfReference(string name) => name is "." or "..";

    /// <summary>
    /// Walks a branch that is not transferred and reports every file below
    /// it as skipped.
    /// </summary>
    private static async Task CollectSkippedAsync(
        SftpTransferRequest request,
        IFileSystem source,
        string directory,
        ScanResult scan,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SftpEntry> entries;
        try
        {
            entries = await source.ListAsync(directory, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSelfReference(entry.Name))
            {
                continue;
            }

            if (scan.SkippedPaths.Count >= request.Options.MaxFiles)
            {
                return;
            }

            var isDirectory = entry.IsDirectory;
            if (entry.IsSymbolicLink)
            {
                isDirectory = await IsDirectoryAsync(source, entry.Path, cancellationToken);
            }

            if (isDirectory)
            {
                await CollectSkippedAsync(request, source, entry.Path, scan, cancellationToken);
                continue;
            }

            scan.SkippedPaths.Add(entry.Path);
        }
    }

    private static async Task CreateDirectoriesAsync(
        IFileSystem target,
        List<string> directories,
        CancellationToken cancellationToken)
    {
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A directory that is already there is reused.
            if (await target.ExistsAsync(directory, cancellationToken))
            {
                continue;
            }

            await target.CreateDirectoryAsync(directory, cancellationToken);
        }
    }

    private static async Task<SftpConflictDecision> ResolveConflictAsync(
        SftpTransferOptions options,
        IFileSystem target,
        TransferItem file,
        ScanResult scan,
        CancellationToken cancellationToken)
    {
        var resolver = options.ConflictResolver;
        if (resolver is null)
        {
            scan.SkippedPaths.Add(file.SourcePath);
            return new SftpConflictDecision(SftpConflictAction.Skip);
        }

        var targetEntry = await FindEntryAsync(target, file.TargetPath, cancellationToken);
        var suggested = await SuggestRenameAsync(target, file.TargetPath, cancellationToken);

        var context = new SftpConflictContext(
            file.SourcePath,
            file.TargetPath,
            targetEntry?.Size ?? 0,
            targetEntry?.LastWriteTimeUtc ?? DateTime.MinValue,
            suggested);

        var decision = await resolver(context);

        if (decision.Action == SftpConflictAction.Skip)
        {
            scan.SkippedPaths.Add(file.SourcePath);
        }

        if (decision.Action == SftpConflictAction.Rename && string.IsNullOrWhiteSpace(decision.NewName))
        {
            decision = decision with { NewName = suggested };
        }

        return decision;
    }

    private static async Task<long> CopyAsync(
        IFileSystem source,
        IFileSystem target,
        TransferItem file,
        string targetPath,
        int completedFiles,
        int totalFiles,
        long totalBytes,
        long completedBytesBefore,
        IProgress<SftpTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var input = await source.OpenReadAsync(file.SourcePath, cancellationToken);
        await using var output = await target.OpenWriteAsync(targetPath, cancellationToken);

        var buffer = new byte[BufferSize];
        var copied = 0L;
        int read;

        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;

            progress?.Report(new SftpTransferProgress(
                SftpTransferStage.Transferring,
                file.FileName,
                completedFiles,
                totalFiles,
                completedBytesBefore + copied,
                totalBytes));

            cancellationToken.ThrowIfCancellationRequested();
        }

        return copied;
    }

    private static async Task<bool> IsDirectoryAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await fileSystem.ListAsync(path, cancellationToken);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static async Task<bool> IsSymbolicLinkAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = await FindEntryAsync(fileSystem, path, cancellationToken);
        return entry?.IsSymbolicLink ?? false;
    }

    private static async Task<long> SizeOfAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = await FindEntryAsync(fileSystem, path, cancellationToken);
        if (entry is null || entry.IsDirectory)
        {
            return 0;
        }

        return entry.Size;
    }

    private static async Task<SftpEntry?> FindEntryAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        var parent = ParentOf(path);
        if (parent.Length == 0)
        {
            return null;
        }

        try
        {
            var entries = await fileSystem.ListAsync(parent, cancellationToken);
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Path, path, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        return null;
    }

    private static async Task<string> SuggestRenameAsync(
        IFileSystem target,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var name = FileNameOf(targetPath);
        var directory = ParentOf(targetPath);

        for (var index = 1; index <= MaxRenameSuggestions; index++)
        {
            var candidate = SftpConflictNaming.Suggest(name, index);
            var candidatePath = CombineTarget(directory, candidate);
            if (!await target.ExistsAsync(candidatePath, cancellationToken))
            {
                return candidate;
            }
        }

        return SftpConflictNaming.Suggest(name, MaxRenameSuggestions);
    }

    private static async Task TryDeleteAsync(IFileSystem target, string path)
    {
        try
        {
            await target.DeleteFileAsync(path, CancellationToken.None);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Returns the last segment of a path. Both separators are accepted, so
    /// that a local Windows path and a remote POSIX path work the same.
    /// </summary>
    private static string FileNameOf(string path)
    {
        var index = path.LastIndexOfAny(PathSeparators);
        return index < 0 ? path : path[(index + 1)..];
    }

    private static string ParentOf(string path)
    {
        var index = path.LastIndexOfAny(PathSeparators);
        if (index < 0)
        {
            return string.Empty;
        }

        if (index == 0)
        {
            return path[..1];
        }

        if (path[index - 1] == ':')
        {
            return path[..(index + 1)];
        }

        return path[..index];
    }

    private static string CombineTarget(string directory, string name)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }

        var separator = directory.Contains('\\') && !directory.Contains('/') ? '\\' : '/';
        var trimmed = directory.TrimEnd('/', '\\');
        return trimmed.Length == 0
            ? separator + name
            : trimmed + separator + name;
    }

    private sealed class TransferItem
    {
        public TransferItem(string sourcePath, string targetPath, string fileName, long size)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            FileName = fileName;
            Size = size;
        }

        public string SourcePath { get; }

        public string TargetPath { get; }

        public string FileName { get; }

        public long Size { get; }
    }

    private sealed class ScanResult
    {
        public List<TransferItem> Files { get; } = new();

        public List<string> Directories { get; } = new();

        public List<string> SkippedPaths { get; } = new();
    }
}
