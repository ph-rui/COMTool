using COMTool.Sftp;
using COMTool.Sftp.Testing;

namespace COMTool.Sftp.Tests;

public sealed class SftpTransferEngineTests
{
    private static (InMemoryFileSystem Remote, InMemoryFileSystem Local) NewSides()
    {
        return (new InMemoryFileSystem("/remote"), new InMemoryFileSystem("/local"));
    }

    private static SftpTransferRequest Upload(
        string targetDirectory,
        params string[] sourcePaths)
    {
        return new SftpTransferRequest(
            SftpTransferDirection.Upload,
            sourcePaths,
            targetDirectory,
            new SftpTransferOptions());
    }

    private static SftpTransferRequest Download(
        string targetDirectory,
        params string[] sourcePaths)
    {
        return new SftpTransferRequest(
            SftpTransferDirection.Download,
            sourcePaths,
            targetDirectory,
            new SftpTransferOptions());
    }

    private static SftpTransferRequest WithOptions(
        SftpTransferRequest request,
        SftpTransferOptions options)
    {
        return request with { Options = options };
    }

    private sealed class RecordingProgress : IProgress<SftpTransferProgress>
    {
        public List<SftpTransferProgress> Events { get; } = new();

        public Action<SftpTransferProgress>? OnEvent { get; set; }

        public void Report(SftpTransferProgress value)
        {
            Events.Add(value);
            OnEvent?.Invoke(value);
        }
    }

    /// <summary>
    /// A remote that rejects one file and that lists a file that is not
    /// really there, the way a file that vanished between the listing and
    /// the transfer behaves.
    /// </summary>
    private sealed class FaultyRemote : ISftpFileSystem
    {
        private readonly InMemoryFileSystem _inner;
        private readonly string _deniedPath;
        private readonly string[] _phantomNames;

        public FaultyRemote(
            InMemoryFileSystem inner,
            string deniedPath,
            params string[] phantomNames)
        {
            _inner = inner;
            _deniedPath = deniedPath;
            _phantomNames = phantomNames;
        }

        public string HomeDirectory => _inner.HomeDirectory;

        public Task<IReadOnlyList<SftpEntry>> ListAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var entries = _inner.ListAsync(path, cancellationToken)
                .GetAwaiter()
                .GetResult()
                .ToList();

            foreach (var name in _phantomNames)
            {
                entries.Add(new SftpEntry(
                    name,
                    SftpPath.Combine(path, name),
                    false,
                    false,
                    3,
                    DateTime.UtcNow,
                    SftpPermission.FromOctalText("644")));
            }

            return Task.FromResult<IReadOnlyList<SftpEntry>>(entries);
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            if (string.Equals(path, _deniedPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("denied");
            }

            if (_phantomNames.Contains(SftpPath.GetFileName(path), StringComparer.Ordinal))
            {
                throw new FileNotFoundException($"The path '{path}' was not found.", path);
            }

            return _inner.OpenReadAsync(path, cancellationToken);
        }

        public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
            _inner.OpenWriteAsync(path, cancellationToken);

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) =>
            _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken) =>
            _inner.RenameAsync(oldPath, newPath, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken) =>
            _inner.DeleteFileAsync(path, cancellationToken);

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken) =>
            _inner.DeleteDirectoryAsync(path, recursive, cancellationToken);

        public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken) =>
            _inner.SetPermissionsAsync(path, octalMode, cancellationToken);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(path, cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    [Fact]
    public async Task UploadsASingleFileByteIdentically()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "hello");

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/a.txt"), null, CancellationToken.None);

        Assert.Equal("hello", remote.ReadFile("/remote/a.txt"));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(5, result.BytesTransferred);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task DownloadsASingleFileByteIdentically()
    {
        var (remote, local) = NewSides();
        remote.AddFile("/remote/a.txt", "hello");

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Download("/local", "/remote/a.txt"), null, CancellationToken.None);

        Assert.Equal("hello", local.ReadFile("/local/a.txt"));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(5, result.BytesTransferred);
    }

    [Fact]
    public async Task UploadsADirectoryTreeAndCreatesTheDirectories()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/tree/sub/b.txt", "b");

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/tree"), null, CancellationToken.None);

        Assert.True(remote.HasEntry("/remote/tree"));
        Assert.True(remote.HasEntry("/remote/tree/sub"));
        Assert.Equal("b", remote.ReadFile("/remote/tree/sub/b.txt"));
        Assert.Equal(1, result.Succeeded);
    }

    [Fact]
    public async Task MergesIntoAnExistingTargetDirectory()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/tree/a.txt", "a");
        local.AddFile("/local/tree/b.txt", "b");
        remote.AddFile("/remote/tree/existing.txt", "e");

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/tree"), null, CancellationToken.None);

        Assert.Equal("a", remote.ReadFile("/remote/tree/a.txt"));
        Assert.Equal("b", remote.ReadFile("/remote/tree/b.txt"));
        Assert.Equal("e", remote.ReadFile("/remote/tree/existing.txt"));
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task AsksTheResolverWhenTheTargetExists()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "new");
        remote.AddFile("/remote/a.txt", "old");

        var asked = new List<SftpConflictContext>();
        var request = WithOptions(
            Upload("/remote", "/local/a.txt"),
            new SftpTransferOptions(
                ConflictResolver: context =>
                {
                    asked.Add(context);
                    return Task.FromResult(new SftpConflictDecision(SftpConflictAction.Overwrite));
                }));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Single(asked);
        Assert.Equal("/remote/a.txt", asked[0].TargetPath);
        Assert.Equal("/local/a.txt", asked[0].SourcePath);
        Assert.Equal("a (1).txt", asked[0].SuggestedRename);
        Assert.Equal("new", remote.ReadFile("/remote/a.txt"));
        Assert.Equal(1, result.Succeeded);
    }

    [Fact]
    public async Task SkipContinuesWithTheRemainingFiles()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "new");
        local.AddFile("/local/b.txt", "b");
        remote.AddFile("/remote/a.txt", "old");

        var request = WithOptions(
            Upload("/remote", "/local/a.txt", "/local/b.txt"),
            new SftpTransferOptions(
                ConflictResolver: _ => Task.FromResult(new SftpConflictDecision(SftpConflictAction.Skip))));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Equal("old", remote.ReadFile("/remote/a.txt"));
        Assert.Equal("b", remote.ReadFile("/remote/b.txt"));
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task CancelAbortsTheWholeQueue()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "new");
        local.AddFile("/local/b.txt", "b");
        remote.AddFile("/remote/a.txt", "old");

        var request = WithOptions(
            Upload("/remote", "/local/a.txt", "/local/b.txt"),
            new SftpTransferOptions(
                ConflictResolver: _ => Task.FromResult(new SftpConflictDecision(SftpConflictAction.Cancel))));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Succeeded);
        Assert.False(remote.HasEntry("/remote/b.txt"));
    }

    [Fact]
    public async Task RenameWritesToTheSuggestedName()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "new");
        remote.AddFile("/remote/a.txt", "old");

        var request = WithOptions(
            Upload("/remote", "/local/a.txt"),
            new SftpTransferOptions(
                ConflictResolver: _ => Task.FromResult(new SftpConflictDecision(SftpConflictAction.Rename))));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Equal("new", remote.ReadFile("/remote/a (1).txt"));
        Assert.Equal("old", remote.ReadFile("/remote/a.txt"));
        Assert.Equal(1, result.Succeeded);
    }

    [Fact]
    public async Task ReportsProgressWithTheTotalFileCountAndSize()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/a.txt", "12345");
        local.AddFile("/local/b.txt", "678");

        var progress = new RecordingProgress();
        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(
                Upload("/remote", "/local/a.txt", "/local/b.txt"),
                progress,
                CancellationToken.None);

        var transferring = progress.Events
            .Where(entry => entry.Stage == SftpTransferStage.Transferring)
            .ToList();

        Assert.NotEmpty(transferring);
        Assert.All(transferring, entry => Assert.Equal(2, entry.TotalFiles));
        Assert.All(transferring, entry => Assert.Equal(8, entry.TotalBytes));
        for (var index = 1; index < transferring.Count; index++)
        {
            Assert.True(transferring[index].CompletedFiles >= transferring[index - 1].CompletedFiles);
        }

        Assert.Equal(SftpTransferStage.Completed, progress.Events[^1].Stage);
        Assert.Equal(2, progress.Events[^1].CompletedFiles);
        Assert.Equal(2, result.Succeeded);
    }

    [Fact]
    public async Task CancellingRemovesThePartiallyWrittenFile()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/big.bin", new string('x', 256 * 1024));

        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress
        {
            OnEvent = entry =>
            {
                if (entry.Stage == SftpTransferStage.Transferring
                    && entry.CompletedBytes >= 64 * 1024)
                {
                    cancellation.Cancel();
                }
            },
        };

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/big.bin"), progress, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.False(remote.HasEntry("/remote/big.bin"));
    }

    [Fact]
    public async Task OneFailingFileDoesNotStopTheQueue()
    {
        var (remote, local) = NewSides();
        remote.AddFile("/remote/ok.txt", "ok");
        remote.AddFile("/remote/denied.txt", "denied");
        var faulty = new FaultyRemote(remote, "/remote/denied.txt", "ghost.txt");

        var result = await new SftpTransferEngine(faulty, local)
            .RunAsync(Download("/local", "/remote"), null, CancellationToken.None);

        // The downloaded directory is created inside the target directory.
        Assert.Equal("ok", local.ReadFile("/local/remote/ok.txt"));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains(result.Failures, failure => failure.Path == "/remote/denied.txt");
        Assert.Contains(result.Failures, failure => failure.Path == "/remote/ghost.txt");
    }

    [Fact]
    public async Task FollowsSymbolicLinksWhenTheOptionIsOn()
    {
        var (remote, local) = NewSides();
        remote.AddDirectory("/remote/real");
        remote.AddFile("/remote/real/inner.txt", "inner");
        remote.AddSymlink("/remote/link", "/remote/real");

        var request = WithOptions(
            Download("/local", "/remote/link"),
            new SftpTransferOptions(FollowSymbolicLinks: true));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Equal("inner", local.ReadFile("/local/link/inner.txt"));
        Assert.Equal(1, result.Succeeded);
    }

    [Fact]
    public async Task SkipsSymbolicLinksWhenTheOptionIsOff()
    {
        var (remote, local) = NewSides();
        remote.AddDirectory("/remote/real");
        remote.AddFile("/remote/real/inner.txt", "inner");
        remote.AddSymlink("/remote/link", "/remote/real");

        var request = WithOptions(
            Download("/local", "/remote/link"),
            new SftpTransferOptions(FollowSymbolicLinks: false));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Contains("/remote/link", result.SkippedPaths);
        Assert.False(local.HasEntry("/local/link"));
        Assert.Equal(0, result.Succeeded);
    }

    [Fact]
    public async Task StopsAtTheMaximumRecursionDepth()
    {
        var (remote, local) = NewSides();
        var current = "/local/tree";
        local.AddDirectory(current);
        local.AddFile(current + "/f.txt", "x");
        for (var index = 1; index <= 19; index++)
        {
            current = current + "/d" + index;
            local.AddFile(current + "/f.txt", "x");
        }

        // The tree is a chain: tree/d1/d2/... The level of the file in
        // "tree/d1/.../d15" is 16, so that is the last level that is
        // transferred.
        static string Chain(int index) => "/tree"
            + string.Concat(Enumerable.Range(1, index).Select(step => "/d" + step));

        var request = WithOptions(
            Upload("/remote", "/local/tree"),
            new SftpTransferOptions(MaxDepth: 16));

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Equal(16, result.Succeeded);
        Assert.Equal(4, result.Skipped);
        Assert.Contains("/local" + Chain(16) + "/f.txt", result.SkippedPaths);
        Assert.Contains("/local" + Chain(19) + "/f.txt", result.SkippedPaths);
        Assert.Equal("x", remote.ReadFile("/remote/tree/f.txt"));
        Assert.Equal("x", remote.ReadFile("/remote" + Chain(15) + "/f.txt"));
        Assert.False(remote.HasEntry("/remote" + Chain(16) + "/f.txt"));
    }

    [Fact]
    public async Task PreservesUnusualFileNames()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/odd/a file.txt", "1");
        local.AddFile("/local/odd/测试.txt", "2");
        local.AddFile("/local/odd/-leading.txt", "3");

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/odd"), null, CancellationToken.None);

        Assert.Equal("1", remote.ReadFile("/remote/odd/a file.txt"));
        Assert.Equal("2", remote.ReadFile("/remote/odd/测试.txt"));
        Assert.Equal("3", remote.ReadFile("/remote/odd/-leading.txt"));
        Assert.Equal(3, result.Succeeded);
    }

    [Fact]
    public async Task TransfersAZeroByteFile()
    {
        var (remote, local) = NewSides();
        local.AddFile("/local/empty.txt", string.Empty);

        var result = await new SftpTransferEngine(remote, local)
            .RunAsync(Upload("/remote", "/local/empty.txt"), null, CancellationToken.None);

        Assert.Equal(string.Empty, remote.ReadFile("/remote/empty.txt"));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.BytesTransferred);
    }
}
