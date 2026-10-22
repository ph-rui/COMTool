using System.Collections.ObjectModel;
using Avalonia.Threading;
using COMTool.Sftp;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

/// <summary>
/// The state of the SFTP drawer of one SSH session: the listing of the
/// remote side and of the local side, the transfers and the questions
/// that a transfer asks.
/// </summary>
public sealed partial class SftpPaneViewModel : ObservableObject
{
    private const double DefaultPaneWidth = 420;
    private const double MinPaneWidth = 320;
    private const double MaxPaneWidth = 640;

    private Func<ISftpFileSystem> _remoteFactory;
    private readonly ILocalFileSystem _local;

    private ISftpFileSystem? _remote;
    private CancellationTokenSource? _transferCts;
    private TaskCompletionSource<SftpConflictDecision>? _conflictCompletion;
    private SftpConflictDecision? _transferDecision;
    private SftpConflictDecision? _sessionDecision;

    private bool _isBusy;
    private bool _isTransferring;
    private string _statusMessage = string.Empty;
    private double _transferProgress;
    private bool _isConflictVisible;
    private string _conflictMessage = string.Empty;
    private bool _hasRemoteError;
    private double _paneWidth = DefaultPaneWidth;

    [ObservableProperty]
    private string _remotePath = string.Empty;

    [ObservableProperty]
    private string _localPath = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private SftpEntry? _selectedRemoteEntry;

    [ObservableProperty]
    private SftpEntry? _selectedLocalEntry;

    [ObservableProperty]
    private bool _followSymbolicLinks = true;

    [ObservableProperty]
    private bool _showHidden = true;

    [ObservableProperty]
    private string _conflictRenameText = string.Empty;

    [ObservableProperty]
    private bool _applyToAll;

    [ObservableProperty]
    private bool _neverAskAgain;

    /// <summary>
    /// Creates the view model of one drawer. The factory is asked for the
    /// remote side when the drawer loads, so that creating the view model
    /// touches no network.
    /// </summary>
    /// <param name="remoteFactory">Hands out the remote file system.</param>
    /// <param name="local">
    /// The local side. When it is left out the real file system of the
    /// machine is used.
    /// </param>
    public SftpPaneViewModel(Func<ISftpFileSystem> remoteFactory, ILocalFileSystem? local = null)
    {
        _remoteFactory = remoteFactory;
        _local = local ?? new SystemLocalFileSystem();
    }

    /// <summary>
    /// The content of the remote directory.
    /// </summary>
    public ObservableCollection<SftpEntry> RemoteEntries { get; } = new();

    /// <summary>
    /// The content of the local directory.
    /// </summary>
    public ObservableCollection<SftpEntry> LocalEntries { get; } = new();

    /// <summary>
    /// The width of the panel in the window. It is kept when the panel is
    /// folded away.
    /// </summary>
    public double PaneWidth
    {
        get => _paneWidth;
        set
        {
            var clamped = Math.Clamp(value, MinPaneWidth, MaxPaneWidth);
            if (SetProperty(ref _paneWidth, clamped))
            {
                OnPropertyChanged(nameof(PaneWidth));
            }
        }
    }

    /// <summary>
    /// True when the remote side could not be reached. The panel then
    /// offers the retry button.
    /// </summary>
    public bool HasRemoteError
    {
        get => _hasRemoteError;
        private set => SetProperty(ref _hasRemoteError, value);
    }

    /// <summary>
    /// True while the drawer reads or writes something.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// True while a transfer runs.
    /// </summary>
    public bool IsTransferring
    {
        get => _isTransferring;
        internal set
        {
            if (SetProperty(ref _isTransferring, value))
            {
                OnPropertyChanged(nameof(CanTransfer));
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    /// <summary>
    /// True when a transfer can be started.
    /// </summary>
    public bool CanTransfer =>
        !IsTransferring && (SelectedRemoteEntry is not null || SelectedLocalEntry is not null);

    /// <summary>
    /// True when an upload can be started. An upload takes the entries of
    /// the local side, so a local entry has to be marked and no transfer
    /// may be running: two transfers would share one client.
    /// </summary>
    public bool CanUpload => !IsTransferring && SelectedLocalEntry is not null;

    /// <summary>
    /// True when a download can be started. A download takes the entries of
    /// the remote side, so a remote entry has to be marked and no transfer
    /// may be running: two transfers would share one client.
    /// </summary>
    public bool CanDownload => !IsTransferring && SelectedRemoteEntry is not null;

    /// <summary>
    /// The last thing that happened, shown under the panes.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// How much of the running transfer is done, in percent.
    /// </summary>
    public double TransferProgress
    {
        get => _transferProgress;
        internal set => SetProperty(ref _transferProgress, value);
    }

    /// <summary>
    /// True while the question about an existing target is shown.
    /// </summary>
    public bool IsConflictVisible
    {
        get => _isConflictVisible;
        private set => SetProperty(ref _isConflictVisible, value);
    }

    /// <summary>
    /// The question about an existing target.
    /// </summary>
    public string ConflictMessage
    {
        get => _conflictMessage;
        private set => SetProperty(ref _conflictMessage, value);
    }

    /// <summary>
    /// Loads both sides of the panel. A remote that cannot be reached
    /// leaves the panel usable and tells the reason in the status line.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var remote = await Task.Run(_remoteFactory, cancellationToken);
            _remote = remote;

            RemotePath = remote.HomeDirectory;
            LocalPath = _local.HomeDirectory;

            await RefreshRemoteAsync();
            await RefreshLocalAsync();

            HasRemoteError = false;

            if (StatusMessage.Length > 0)
            {
                StatusMessage = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The loading was cancelled.";
        }
        catch (Exception ex)
        {
            HasRemoteError = true;
            StatusMessage = $"SFTP unavailable: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads the panel when it is shown for the first time, and tries
    /// again after a failure. A panel that already has its remote side
    /// keeps it, so folding and opening the panel asks for nothing new.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _remote is not null)
        {
            return;
        }

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Loads both sides of the drawer again, after a failure of the
    /// remote side.
    /// </summary>
    public async Task RetryAsync()
    {
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Reads the content of the remote directory again.
    /// </summary>
    public async Task RefreshRemoteAsync()
    {
        var remote = _remote;
        if (remote is null)
        {
            return;
        }

        try
        {
            var entries = await remote.ListAsync(RemotePath, CancellationToken.None);
            ApplyEntries(RemoteEntries, entries);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Listing failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the content of the local directory again.
    /// </summary>
    public async Task RefreshLocalAsync()
    {
        try
        {
            var entries = await _local.ListAsync(LocalPath, CancellationToken.None);
            ApplyEntries(LocalEntries, entries);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Listing failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Goes into the directory of the entry. A link to a directory is
    /// entered as well, a link to a file is not.
    /// </summary>
    public async Task EnterRemoteAsync(SftpEntry entry)
    {
        if (entry is null || (!entry.IsDirectory && !entry.IsSymbolicLink))
        {
            return;
        }

        var remote = _remote;
        if (remote is null)
        {
            return;
        }

        if (!entry.IsDirectory)
        {
            try
            {
                await remote.ListAsync(entry.Path, CancellationToken.None);
            }
            catch (Exception)
            {
                // A link that points at a file cannot be entered.
                return;
            }
        }

        RemotePath = entry.Path;
        await RefreshRemoteAsync();
    }

    /// <summary>
    /// Goes into the directory of the local entry.
    /// </summary>
    public async Task EnterLocalAsync(SftpEntry entry)
    {
        if (entry is null || !entry.IsDirectory)
        {
            return;
        }

        LocalPath = entry.Path;
        await RefreshLocalAsync();
    }

    /// <summary>
    /// Goes to the parent of the remote directory. The root is not left.
    /// </summary>
    public async Task GoUpRemoteAsync()
    {
        var parent = SftpPath.GetParent(RemotePath);
        if (parent.Length == 0 || parent == RemotePath)
        {
            return;
        }

        RemotePath = parent;
        await RefreshRemoteAsync();
    }

    /// <summary>
    /// Goes to the parent of the local directory.
    /// </summary>
    public async Task GoUpLocalAsync()
    {
        var parent = Path.GetDirectoryName(LocalPath);
        if (string.IsNullOrEmpty(parent))
        {
            return;
        }

        LocalPath = parent;
        await RefreshLocalAsync();
    }

    /// <summary>
    /// Creates a directory on the remote side.
    /// </summary>
    public async Task NewRemoteFolderAsync(string name)
    {
        var remote = _remote;
        if (remote is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await remote.CreateDirectoryAsync(SftpPath.Combine(RemotePath, name), CancellationToken.None);
            await RefreshRemoteAsync();
            StatusMessage = $"Created {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Renames the selected remote entry.
    /// </summary>
    public async Task RenameRemoteAsync(string newName)
    {
        var remote = _remote;
        var entry = SelectedRemoteEntry;
        if (remote is null || entry is null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            await remote.RenameAsync(entry.Path, SftpPath.Combine(RemotePath, newName), CancellationToken.None);
            SelectedRemoteEntry = null;
            await RefreshRemoteAsync();
            StatusMessage = $"Renamed to {newName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes the selected remote entry. A directory is removed with
    /// everything in it.
    /// </summary>
    public async Task DeleteRemoteAsync()
    {
        var remote = _remote;
        var entry = SelectedRemoteEntry;
        if (remote is null || entry is null)
        {
            return;
        }

        try
        {
            if (entry.IsDirectory)
            {
                await remote.DeleteDirectoryAsync(entry.Path, recursive: true, CancellationToken.None);
            }
            else
            {
                await remote.DeleteFileAsync(entry.Path, CancellationToken.None);
            }

            SelectedRemoteEntry = null;
            await RefreshRemoteAsync();
            StatusMessage = $"Deleted {entry.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Applies a POSIX mode to the selected remote entry. The mode is
    /// given as a number, so 0644 is the number 420.
    /// </summary>
    public async Task SetRemotePermissionsAsync(int octalMode)
    {
        var remote = _remote;
        var entry = SelectedRemoteEntry;
        if (remote is null || entry is null)
        {
            return;
        }

        try
        {
            await remote.SetPermissionsAsync(entry.Path, octalMode, CancellationToken.None);
            await RefreshRemoteAsync();
            StatusMessage = $"Permissions set to {SftpPermission.FromOctal(octalMode).ToOctalText()}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"chmod failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends the selected local entry to the remote side.
    /// </summary>
    public async Task UploadAsync()
    {
        var entry = SelectedLocalEntry;
        if (IsTransferring || entry is null)
        {
            return;
        }

        await RunTransferAsync(SftpTransferDirection.Upload, entry.Path, RemotePath);
    }

    /// <summary>
    /// Takes the selected remote entry to the local side.
    /// </summary>
    public async Task DownloadAsync()
    {
        var entry = SelectedRemoteEntry;
        if (IsTransferring || entry is null)
        {
            return;
        }

        await RunTransferAsync(SftpTransferDirection.Download, entry.Path, LocalPath);
    }

    /// <summary>
    /// Stops the running transfer. The file that is being written is
    /// removed.
    /// </summary>
    public void CancelTransfer()
    {
        _transferCts?.Cancel();
    }

    /// <summary>
    /// Answers the question about an existing target.
    /// </summary>
    public void ResolveConflict(SftpConflictAction action)
    {
        var completion = _conflictCompletion;
        if (completion is null)
        {
            return;
        }

        var decision = new SftpConflictDecision(
            action,
            action == SftpConflictAction.Rename ? ConflictRenameText : null);

        if (action != SftpConflictAction.Cancel)
        {
            if (NeverAskAgain)
            {
                _sessionDecision = decision;
            }
            else if (ApplyToAll)
            {
                _transferDecision = decision;
            }
        }

        _conflictCompletion = null;
        IsConflictVisible = false;
        completion.TrySetResult(decision);
    }

    /// <summary>
    /// Puts another remote side in place of the current one, after the
    /// session was started again. The size and the state of the drawer
    /// stay as they are.
    /// </summary>
    public void SetRemoteFactory(Func<ISftpFileSystem> remoteFactory)
    {
        _remoteFactory = remoteFactory;
        _remote = null;
        RemoteEntries.Clear();
        RemotePath = string.Empty;

        _ = LoadAsync(CancellationToken.None);
    }

    partial void OnShowHiddenChanged(bool value)
    {
        _ = RefreshRemoteAsync();
        _ = RefreshLocalAsync();
    }

    partial void OnSelectedRemoteEntryChanged(SftpEntry? value)
    {
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanDownload));
    }

    partial void OnSelectedLocalEntryChanged(SftpEntry? value)
    {
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanUpload));
    }

    private void ApplyEntries(ObservableCollection<SftpEntry> target, IEnumerable<SftpEntry> entries)
    {
        target.Clear();

        foreach (var entry in entries
            .Where(entry => ShowHidden || !entry.Name.StartsWith('.'))
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(entry);
        }
    }

    private async Task RunTransferAsync(SftpTransferDirection direction, string sourcePath, string targetDirectory)
    {
        var remote = _remote;
        if (IsTransferring || remote is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _transferCts = cts;
        IsTransferring = true;
        IsBusy = true;
        TransferProgress = 0;

        try
        {
            var options = new SftpTransferOptions(
                FollowSymbolicLinks: FollowSymbolicLinks,
                ConflictResolver: ResolveConflictAsync);

            var request = new SftpTransferRequest(direction, new[] { sourcePath }, targetDirectory, options);
            var engine = new SftpTransferEngine(remote, _local);
            var progress = new Progress<SftpTransferProgress>(OnProgress);

            var result = await Task.Run(() => engine.RunAsync(request, progress, cts.Token), cts.Token);

            StatusMessage = Summarize(direction, result);
            if (!result.Cancelled)
            {
                TransferProgress = 100;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Transfer cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Transfer failed: {ex.Message}";
        }
        finally
        {
            IsTransferring = false;
            IsBusy = false;
            _transferCts = null;
            cts.Dispose();
        }

        await RefreshRemoteAsync();
        await RefreshLocalAsync();
    }

    private void OnProgress(SftpTransferProgress report)
    {
        if (!IsTransferring)
        {
            // A report that arrives after the end of the transfer is late
            // and must not overwrite the summary.
            return;
        }

        TransferProgress = report.TotalBytes > 0
            ? (double)report.CompletedBytes / report.TotalBytes * 100
            : 0;

        if (report.Stage == SftpTransferStage.Transferring)
        {
            StatusMessage = $"Transferring {report.FileName}";
        }
    }

    private static string Summarize(SftpTransferDirection direction, SftpTransferResult result)
    {
        var verb = direction == SftpTransferDirection.Upload ? "uploaded" : "downloaded";
        var summary = $"{result.Succeeded} {verb}";

        if (result.Failed > 0)
        {
            summary += $", {result.Failed} failed";
        }

        if (result.Skipped > 0)
        {
            summary += $", {result.Skipped} skipped";
        }

        if (result.Cancelled)
        {
            summary += ", cancelled";
        }

        return summary;
    }

    /// <summary>
    /// Asks the user what to do with a target that already exists. The
    /// engine runs on a background thread, so the question is put up on
    /// the thread of the interface.
    /// </summary>
    private async Task<SftpConflictDecision> ResolveConflictAsync(SftpConflictContext context)
    {
        if (_sessionDecision is not null)
        {
            return _sessionDecision;
        }

        if (_transferDecision is not null)
        {
            return _transferDecision;
        }

        var completion = new TaskCompletionSource<SftpConflictDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _conflictCompletion = completion;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConflictMessage = $"{context.SourcePath} already exists as {context.TargetPath}";
            ConflictRenameText = context.SuggestedRename;
            IsConflictVisible = true;
        });

        return await completion.Task;
    }
}
