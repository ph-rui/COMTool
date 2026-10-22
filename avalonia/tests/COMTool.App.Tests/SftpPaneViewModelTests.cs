using Avalonia.Headless.XUnit;
using COMTool.App.ViewModels;
using COMTool.Sftp;
using COMTool.Sftp.Testing;

namespace COMTool.App.Tests;

public sealed class SftpPaneViewModelTests
{
    private const string RemoteHome = "/remote";
    private const string LocalHome = "/local";

    private static (SftpPaneViewModel ViewModel, InMemoryFileSystem Remote, InMemoryFileSystem Local) NewViewModel()
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        var local = new InMemoryFileSystem(LocalHome);
        var viewModel = new SftpPaneViewModel(() => remote, local);
        return (viewModel, remote, local);
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"The panel did not {what} in time.");
    }

    [AvaloniaFact]
    public void StartsCollapsedWithTheDefaultWidth()
    {
        var (viewModel, _, _) = NewViewModel();

        Assert.False(viewModel.IsExpanded);
        Assert.Equal(420, viewModel.PaneWidth);
    }

    [AvaloniaFact]
    public void ClampsThePaneWidth()
    {
        var (viewModel, _, _) = NewViewModel();

        viewModel.PaneWidth = 10;
        Assert.Equal(320, viewModel.PaneWidth);

        viewModel.PaneWidth = 5000;
        Assert.Equal(640, viewModel.PaneWidth);
    }

    [AvaloniaFact]
    public async Task EnsureLoadedAsyncAsksForTheRemoteOnlyOnce()
    {
        var calls = 0;
        var remote = new InMemoryFileSystem(RemoteHome);
        var viewModel = new SftpPaneViewModel(
            () =>
            {
                calls++;
                return remote;
            },
            new InMemoryFileSystem(LocalHome));

        await viewModel.EnsureLoadedAsync(CancellationToken.None);
        await viewModel.EnsureLoadedAsync(CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(RemoteHome, viewModel.RemotePath);
        Assert.False(viewModel.HasRemoteError);
    }

    [AvaloniaFact]
    public async Task EnsureLoadedAsyncTriesAgainAfterAFailure()
    {
        var calls = 0;
        var remote = new InMemoryFileSystem(RemoteHome);
        var viewModel = new SftpPaneViewModel(
            () =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("the server is away");
                }

                return remote;
            },
            new InMemoryFileSystem(LocalHome));

        await viewModel.EnsureLoadedAsync(CancellationToken.None);

        Assert.True(viewModel.HasRemoteError);
        Assert.Contains("SFTP unavailable", viewModel.StatusMessage);

        await viewModel.EnsureLoadedAsync(CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.False(viewModel.HasRemoteError);
        Assert.Equal(RemoteHome, viewModel.RemotePath);
    }

    [AvaloniaFact]
    public async Task LoadsTheRemoteHomeDirectory()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        remote.AddFile("/remote/b.txt", "b");

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(RemoteHome, viewModel.RemotePath);
        Assert.Equal(LocalHome, viewModel.LocalPath);
        Assert.Equal(new[] { "a.txt", "b.txt" }, viewModel.RemoteEntries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task ReportsAnUnavailableSftpSubsystem()
    {
        var viewModel = new SftpPaneViewModel(
            () => throw new InvalidOperationException("Subsystem 'sftp' could not be executed."),
            new InMemoryFileSystem(LocalHome));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Contains("SFTP unavailable", viewModel.StatusMessage);
        Assert.Contains("could not be executed", viewModel.StatusMessage);
        Assert.Empty(viewModel.RemoteEntries);
    }

    [AvaloniaFact]
    public async Task RetriesAfterAFailure()
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        remote.AddFile("/remote/a.txt", "a");
        var attempts = 0;
        var viewModel = new SftpPaneViewModel(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("the connection was closed");
                }

                return remote;
            },
            new InMemoryFileSystem(LocalHome));

        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Contains("SFTP unavailable", viewModel.StatusMessage);

        await viewModel.RetryAsync();

        Assert.Equal(2, attempts);
        Assert.Equal(new[] { "a.txt" }, viewModel.RemoteEntries.Select(entry => entry.Name));
        Assert.DoesNotContain("SFTP unavailable", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task EntersAndLeavesRemoteDirectories()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddDirectory("/remote/dir");
        remote.AddFile("/remote/dir/inner.txt", "x");
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.EnterRemoteAsync(viewModel.RemoteEntries.Single(entry => entry.Name == "dir"));

        Assert.Equal("/remote/dir", viewModel.RemotePath);
        Assert.Equal(new[] { "inner.txt" }, viewModel.RemoteEntries.Select(entry => entry.Name));

        await viewModel.GoUpRemoteAsync();

        Assert.Equal(RemoteHome, viewModel.RemotePath);
        Assert.Contains(viewModel.RemoteEntries, entry => entry.Name == "dir");
    }

    [AvaloniaFact]
    public async Task HidesHiddenFilesWhenTheToggleIsOff()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddFile("/remote/.hidden", "h");
        remote.AddFile("/remote/visible.txt", "v");
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Contains(viewModel.RemoteEntries, entry => entry.Name == ".hidden");

        viewModel.ShowHidden = false;

        await WaitUntil(
            () => !viewModel.RemoteEntries.Any(entry => entry.Name == ".hidden"),
            "hide the hidden file");
        Assert.Equal(new[] { "visible.txt" }, viewModel.RemoteEntries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task CreatesRenamesAndDeletesOnTheRemote()
    {
        var (viewModel, remote, _) = NewViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NewRemoteFolderAsync("folder");

        Assert.True(remote.HasEntry("/remote/folder"));
        Assert.Equal("Created folder", viewModel.StatusMessage);

        viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "folder");
        await viewModel.RenameRemoteAsync("renamed");

        Assert.True(remote.HasEntry("/remote/renamed"));
        Assert.False(remote.HasEntry("/remote/folder"));
        Assert.Equal("Renamed to renamed", viewModel.StatusMessage);

        viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "renamed");
        await viewModel.DeleteRemoteAsync();

        Assert.False(remote.HasEntry("/remote/renamed"));
        Assert.Equal("Deleted renamed", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public void RefusesToDeleteALocalEntry()
    {
        var names = typeof(SftpPaneViewModel)
            .GetMethods()
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain(names, name => name.Contains("Delete") && name.Contains("Local"));
    }

    [AvaloniaFact]
    public async Task AppliesPermissionsToTheRemoteEntry()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
        await viewModel.SetRemotePermissionsAsync(Convert.ToInt32("640", 8));

        Assert.Equal("640", viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt").Permission.ToOctalText());
        Assert.Equal("Permissions set to 640", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task UploadsTheSelectedLocalEntries()
    {
        var (viewModel, remote, local) = NewViewModel();
        local.AddFile("/local/a.txt", "hello");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
        await viewModel.UploadAsync();

        Assert.Equal("hello", remote.ReadFile("/remote/a.txt"));
        Assert.Contains("1 uploaded", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task DownloadsTheSelectedRemoteEntries()
    {
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/b.txt", "world");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "b.txt");
        await viewModel.DownloadAsync();

        Assert.Equal("world", local.ReadFile("/local/b.txt"));
        Assert.Contains("1 downloaded", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task DisablesTheTransferButtonsWithoutASelection()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.CanTransfer);

        viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.First();
        Assert.True(viewModel.CanTransfer);

        viewModel.SelectedRemoteEntry = null;
        Assert.False(viewModel.CanTransfer);
    }

    [AvaloniaFact]
    public async Task DisablesTheTransferButtonsWhileATransferRuns()
    {
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "old");
        local.AddFile("/local/a.txt", "new");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLocalEntry =
            viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
        viewModel.SelectedRemoteEntry =
            viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");

        var upload = viewModel.UploadAsync();
        await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");

        // The spec disables Upload and Download while a transfer runs: two
        // transfers would share one client and the second one would take
        // the cancellation token of the first.
        Assert.True(viewModel.IsTransferring);
        Assert.False(viewModel.CanUpload);
        Assert.False(viewModel.CanDownload);
        Assert.False(viewModel.CanTransfer);

        var secondUpload = viewModel.UploadAsync();
        Assert.True(secondUpload.IsCompletedSuccessfully);
        var secondDownload = viewModel.DownloadAsync();
        Assert.True(secondDownload.IsCompletedSuccessfully);

        viewModel.ResolveConflict(SftpConflictAction.Overwrite);
        await upload.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(viewModel.IsTransferring);
    }

    [AvaloniaFact]
    public async Task ShowsAConflictPromptAndAppliesTheDecision()
    {
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "old");
        local.AddFile("/local/a.txt", "new");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
        var upload = viewModel.UploadAsync();

        await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");
        Assert.Contains("a (1).txt", viewModel.ConflictRenameText);

        viewModel.ResolveConflict(SftpConflictAction.Overwrite);
        await upload.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("new", remote.ReadFile("/remote/a.txt"));
        Assert.False(viewModel.IsConflictVisible);
    }

    [AvaloniaFact]
    public async Task AppliesTheDecisionToAllRemainingConflicts()
    {
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/batch/a.txt", "old-a");
        remote.AddFile("/remote/batch/b.txt", "old-b");
        local.AddFile("/local/batch/a.txt", "new-a");
        local.AddFile("/local/batch/b.txt", "new-b");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.ApplyToAll = true;
        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "batch");
        var upload = viewModel.UploadAsync();

        await WaitUntil(() => viewModel.IsConflictVisible, "ask about the first conflict");
        viewModel.ResolveConflict(SftpConflictAction.Overwrite);
        await upload.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("new-a", remote.ReadFile("/remote/batch/a.txt"));
        Assert.Equal("new-b", remote.ReadFile("/remote/batch/b.txt"));
        Assert.False(viewModel.IsConflictVisible);
    }

    [AvaloniaFact]
    public async Task RemembersTheNeverAskAgainDecision()
    {
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "old");
        local.AddFile("/local/a.txt", "new");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.NeverAskAgain = true;
        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
        var first = viewModel.UploadAsync();

        await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");
        viewModel.ResolveConflict(SftpConflictAction.Overwrite);
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        var second = viewModel.UploadAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("new", remote.ReadFile("/remote/a.txt"));
        Assert.False(viewModel.IsConflictVisible);
    }

    [AvaloniaFact]
    public async Task ReportsAFailedTransferInTheStatusMessage()
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        var local = new InMemoryFileSystem(LocalHome);
        local.AddFile("/local/a.txt", "hello");
        var viewModel = new SftpPaneViewModel(() => new FailingRemoteFileSystem(remote), local);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
        await viewModel.UploadAsync();

        Assert.Contains("failed", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task CancellingTheTransferStopsTheQueueAndRemovesThePartialFile()
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        var local = new InMemoryFileSystem(LocalHome);
        local.AddFile("/local/big.bin", new string('x', 1024 * 1024));
        var viewModel = new SftpPaneViewModel(() => remote, new SlowLocalFileSystem(local, 20));
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "big.bin");
        var upload = viewModel.UploadAsync();

        await Task.Delay(60);
        viewModel.CancelTransfer();
        await upload.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("cancelled", viewModel.StatusMessage);
        Assert.False(remote.HasEntry("/remote/big.bin"));
    }

    [AvaloniaFact]
    public async Task SwapsTheRemoteFactoryAfterASessionRestart()
    {
        var (viewModel, remote, _) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.IsExpanded = false;
        viewModel.PaneWidth = 500;

        var other = new InMemoryFileSystem("/other");
        other.AddFile("/other/b.txt", "b");
        viewModel.SetRemoteFactory(() => other);

        await WaitUntil(
            () => viewModel.RemoteEntries.Count == 1 && viewModel.RemoteEntries[0].Name == "b.txt",
            "load the new remote");
        Assert.Equal("/other", viewModel.RemotePath);
        Assert.False(viewModel.IsExpanded);
        Assert.Equal(500, viewModel.PaneWidth);
    }

    /// <summary>
    /// A remote that refuses to write, so that the status message of a
    /// failed transfer can be seen.
    /// </summary>
    private sealed class FailingRemoteFileSystem : ISftpFileSystem
    {
        private readonly ISftpFileSystem _inner;

        public FailingRemoteFileSystem(ISftpFileSystem inner)
        {
            _inner = inner;
        }

        public string HomeDirectory => _inner.HomeDirectory;

        public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
            => _inner.ListAsync(path, cancellationToken);

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
            => _inner.OpenReadAsync(path, cancellationToken);

        public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
            => throw new IOException("the disk of the server is full");

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
            => _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
            => _inner.RenameAsync(oldPath, newPath, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
            => _inner.DeleteFileAsync(path, cancellationToken);

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
            => _inner.DeleteDirectoryAsync(path, recursive, cancellationToken);

        public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
            => _inner.SetPermissionsAsync(path, octalMode, cancellationToken);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => _inner.ExistsAsync(path, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A local side that reads slowly, so that a transfer can be
    /// cancelled while it runs.
    /// </summary>
    private sealed class SlowLocalFileSystem : ILocalFileSystem
    {
        private readonly ILocalFileSystem _inner;
        private readonly int _delayMilliseconds;

        public SlowLocalFileSystem(ILocalFileSystem inner, int delayMilliseconds)
        {
            _inner = inner;
            _delayMilliseconds = delayMilliseconds;
        }

        public string HomeDirectory => _inner.HomeDirectory;

        public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
            => _inner.ListAsync(path, cancellationToken);

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
            => WrapAsync(_inner.OpenReadAsync(path, cancellationToken));

        public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
            => _inner.OpenWriteAsync(path, cancellationToken);

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
            => _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
            => _inner.RenameAsync(oldPath, newPath, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
            => _inner.DeleteFileAsync(path, cancellationToken);

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
            => _inner.DeleteDirectoryAsync(path, recursive, cancellationToken);

        public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
            => _inner.SetPermissionsAsync(path, octalMode, cancellationToken);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => _inner.ExistsAsync(path, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async Task<Stream> WrapAsync(Task<Stream> open)
        {
            return new SlowStream(await open, _delayMilliseconds);
        }
    }

    /// <summary>
    /// A stream that waits before every read.
    /// </summary>
    private sealed class SlowStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _delayMilliseconds;

        public SlowStream(Stream inner, int delayMilliseconds)
        {
            _inner = inner;
            _delayMilliseconds = delayMilliseconds;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMilliseconds, cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
