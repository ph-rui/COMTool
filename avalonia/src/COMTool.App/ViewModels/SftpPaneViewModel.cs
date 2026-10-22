using System.Collections.ObjectModel;
using Avalonia.Threading;
using COMTool.Sftp;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

/// <summary>
/// The order in which the remote list is shown, set by the column
/// headers.
/// </summary>
public enum SftpSortColumn
{
    Name,
    Size,
    Modified,
    Permissions,
}

/// <summary>
/// The state of the SFTP panel of one SSH session: the listing of the
/// remote side, the transfers and the questions that a transfer asks.
/// The panel has one pane, like the MobaXterm side panel; the local side of
/// an upload or a download comes from a system dialog and is not listed
/// here.
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
    private IReadOnlyList<SftpEntry> _remoteListing = Array.Empty<SftpEntry>();

    private bool _isBusy;
    private bool _isTransferring;
    private bool _hasRemoteError;
    private string _statusMessage = string.Empty;
    private double _transferProgress;
    private bool _isConflictVisible;
    private string _conflictMessage = string.Empty;
    private double _paneWidth = DefaultPaneWidth;
    private SftpSortColumn _sortColumn = SftpSortColumn.Name;
    private bool _sortDescending;

    [ObservableProperty]
    private string _remotePath = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private SftpEntry? _selectedRemoteEntry;

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
    /// Creates the view model of one panel. The factory is asked for the
    /// remote side when the panel loads, so that creating the view model
    /// touches no network.
    /// </summary>
    /// <param name="remoteFactory">Hands out the remote file system.</param>
    /// <param name="local">
    /// The local file system that reads the files of an upload and writes
    /// the files of a download. When it is left out the real file system
    /// of the machine is used.
    /// </param>
    public SftpPaneViewModel(Func<ISftpFileSystem> remoteFactory, ILocalFileSystem? local = null)
    {
        _remoteFactory = remoteFactory;
        _local = local ?? new SystemLocalFileSystem();
    }

    /// <summary>
    /// The content of the remote directory, in the order of the column
    /// headers.
    /// </summary>
    public ObservableCollection<SftpEntry> RemoteEntries { get; } = new();

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
    /// The column that the list is ordered by.
    /// </summary>
    public SftpSortColumn SortColumn
    {
        get => _sortColumn;
        private set
        {
            if (SetProperty(ref _sortColumn, value))
            {
                OnPropertyChanged(nameof(NameSortGlyph));
                OnPropertyChanged(nameof(SizeSortGlyph));
                OnPropertyChanged(nameof(ModifiedSortGlyph));
                OnPropertyChanged(nameof(PermissionsSortGlyph));
            }
        }
    }

    /// <summary>
    /// True when the list is ordered from the largest value to the
    /// smallest.
    /// </summary>
    public bool SortDescending
    {
        get => _sortDescending;
        private set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                OnPropertyChanged(nameof(NameSortGlyph));
                OnPropertyChanged(nameof(SizeSortGlyph));
                OnPropertyChanged(nameof(ModifiedSortGlyph));
                OnPropertyChanged(nameof(PermissionsSortGlyph));
            }
        }
    }

    public string NameSortGlyph => SortColumn == SftpSortColumn.Name ? DirectionGlyph() : string.Empty;

    public string SizeSortGlyph => SortColumn == SftpSortColumn.Size ? DirectionGlyph() : string.Empty;

    public string ModifiedSortGlyph => SortColumn == SftpSortColumn.Modified ? DirectionGlyph() : string.Empty;

    public string PermissionsSortGlyph => SortColumn == SftpSortColumn.Permissions ? DirectionGlyph() : string.Empty;

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
    /// True while the panel reads or writes something.
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
    public bool CanTransfer => !IsTransferring && SelectedRemoteEntry is not null;

    /// <summary>
    /// True when an upload can be started. The files of an upload come
    /// from the system dialog, so no selection is needed; two transfers
    /// would share one client, so only one may run.
    /// </summary>
    public bool CanUpload => !IsTransferring;

    /// <summary>
    /// True when a download can be started. A download takes the marked
    /// entry of the remote list and no transfer may be running.
    /// </summary>
    public bool CanDownload => !IsTransferring && SelectedRemoteEntry is not null;

    /// <summary>
    /// The last thing that happened, shown under the list.
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
    /// Loads the remote side of the panel. A remote that cannot be reached
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

            await RefreshRemoteAsync();

            HasRemoteError = false;

            if (StatusMessage.Length > 0 && StatusMessage.StartsWith("SFTP unavailable", StringComparison.Ordinal))
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
    /// Loads the remote side again, after a failure.
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
            _remoteListing = entries;
            ApplyEntries();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Listing failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Orders the list by the given column. A second click on the same
    /// column turns the order around.
    /// </summary>
    public void SortBy(SftpSortColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        ApplyEntries();
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
    /// Sends the given local files into the directory that the panel
    /// shows. The files come from the system dialog of the upload button.
    /// </summary>
    public async Task UploadAsync(IReadOnlyList<string> localPaths)
    {
        if (IsTransferring || localPaths.Count == 0)
        {
            return;
        }

        await RunTransferAsync(SftpTransferDirection.Upload, localPaths, RemotePath);
    }

    /// <summary>
    /// Takes the selected remote entry into the given local directory.
    /// The directory comes from the system dialog of the download button.
    /// </summary>
    public async Task DownloadAsync(string targetDirectory)
    {
        var entry = SelectedRemoteEntry;
        if (IsTransferring || entry is null || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return;
        }

        await RunTransferAsync(SftpTransferDirection.Download, new[] { entry.Path }, targetDirectory);
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
    /// session was started again. The size and the state of the panel
    /// stay as they are.
    /// </summary>
    public void SetRemoteFactory(Func<ISftpFileSystem> remoteFactory)
    {
        _remoteFactory = remoteFactory;
        _remote = null;
        _remoteListing = Array.Empty<SftpEntry>();
        RemoteEntries.Clear();
        RemotePath = string.Empty;

        _ = LoadAsync(CancellationToken.None);
    }

    partial void OnShowHiddenChanged(bool value)
    {
        ApplyEntries();
    }

    partial void OnSelectedRemoteEntryChanged(SftpEntry? value)
    {
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanDownload));
    }

    private string DirectionGlyph() => SortDescending ? "\u25bc" : "\u25b2";

    /// <summary>
    /// Fills the list from the last listing: hidden entries are filtered,
    /// directories come first, and the column headers decide the order.
    /// The marked entry stays marked when it is still there.
    /// </summary>
    private void ApplyEntries()
    {
        var selectedPath = SelectedRemoteEntry?.Path;

        var entries = _remoteListing
            .Where(entry => ShowHidden || !entry.Name.StartsWith('.'))
            .ToList();

        entries.Sort(CompareEntries);

        RemoteEntries.Clear();
        foreach (var entry in entries)
        {
            RemoteEntries.Add(entry);
        }

        if (selectedPath is not null)
        {
            SelectedRemoteEntry = RemoteEntries.FirstOrDefault(entry => entry.Path == selectedPath);
        }
    }

    private int CompareEntries(SftpEntry left, SftpEntry right)
    {
        var byKind = right.IsDirectory.CompareTo(left.IsDirectory);
        if (byKind != 0)
        {
            return byKind;
        }

        var result = SortColumn switch
        {
            SftpSortColumn.Size => left.Size.CompareTo(right.Size),
            SftpSortColumn.Modified => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc),
            SftpSortColumn.Permissions => string.Compare(
                left.Permission.ToOctalText(),
                right.Permission.ToOctalText(),
                StringComparison.Ordinal),
            _ => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
        };

        return SortDescending ? -result : result;
    }

    private async Task RunTransferAsync(
        SftpTransferDirection direction,
        IReadOnlyList<string> sourcePaths,
        string targetDirectory)
    {
        var remote = _remote;
        if (IsTransferring || remote is null || sourcePaths.Count == 0)
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

            var request = new SftpTransferRequest(direction, sourcePaths, targetDirectory, options);
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
