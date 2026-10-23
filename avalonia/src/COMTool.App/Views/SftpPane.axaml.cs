using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// The panel of an SSH session: the entries of the remote directory, the
/// icon toolbar above them and the state of the transfer below. The panel
/// has one list, like the side panel of MobaXterm; the local side of an
/// upload or a download comes from a system dialog.
/// </summary>
public sealed partial class SftpPane : UserControl
{
    private enum PromptMode
    {
        None,
        NewFolder,
        Rename,
    }

    private SftpPaneViewModel? _viewModel;
    private SftpPermissionsWindow? _permissionsWindow;
    private ILocalPickers? _pickers;
    private PromptMode _promptMode;
    private bool _deleteArmed;

    public SftpPane()
    {
        InitializeComponent();

        UpdateCommandState();
    }

    /// <summary>
    /// The state of the panel. It becomes the context of the view, so that
    /// every binding inside the panel finds it.
    /// </summary>
    public SftpPaneViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value;
            DataContext = value;

            if (value is not null)
            {
                value.PropertyChanged += OnViewModelPropertyChanged;
            }

            HidePrompt();
            DisarmDelete();
            UpdateCommandState();
        }
    }

    /// <summary>
    /// The dialogs of the upload and of the download. A test puts its own
    /// answer here; when nothing is put, the system dialogs are asked.
    /// </summary>
    public ILocalPickers? Pickers
    {
        get => _pickers;
        set => _pickers = value;
    }

    internal ListBox RemoteListControl => RemoteList;

    internal TextBox RemotePathBoxControl => RemotePathBox;

    internal Button ParentRowButtonControl => ParentRowButton;

    internal Button PathMenuButtonControl => PathMenuButton;

    internal Button RefreshButtonControl => RefreshButton;

    internal Button NewFolderButtonControl => NewFolderButton;

    internal Button RenameButtonControl => RenameButton;

    internal Button DeleteButtonControl => DeleteButton;

    internal Button ChmodButtonControl => ChmodButton;

    internal Button UploadButtonControl => UploadButton;

    internal Button DownloadButtonControl => DownloadButton;

    internal ToggleButton FollowLinksButtonControl => FollowLinksButton;

    internal ToggleButton ShowHiddenButtonControl => ShowHiddenButton;

    internal Border PromptRowControl => PromptRow;

    internal TextBlock PromptLabelControl => PromptLabel;

    internal TextBox PromptBoxControl => PromptBox;

    internal Button PromptOkButtonControl => PromptOkButton;

    internal Button PromptCancelButtonControl => PromptCancelButton;

    internal Button NameHeaderButtonControl => NameHeaderButton;

    internal Button SizeHeaderButtonControl => SizeHeaderButton;

    internal Button ModifiedHeaderButtonControl => ModifiedHeaderButton;

    internal Button PermissionsHeaderButtonControl => PermissionsHeaderButton;

    internal TextBlock OwnerHeaderControl => OwnerHeader;

    internal TextBlock NameSortGlyphControl => NameSortGlyph;

    internal TextBlock SizeSortGlyphControl => SizeSortGlyph;

    internal TextBlock ModifiedSortGlyphControl => ModifiedSortGlyph;

    internal TextBlock PermissionsSortGlyphControl => PermissionsSortGlyph;

    internal Button CancelButtonControl => CancelButton;

    internal Button RetryButtonControl => RetryButton;

    internal ProgressBar TransferProgressBarControl => TransferProgressBar;

    internal Border ConflictPanelControl => ConflictPanel;

    internal TextBox ConflictRenameBoxControl => ConflictRenameBox;

    internal CheckBox ApplyToAllBoxControl => ApplyToAllBox;

    internal CheckBox NeverAskAgainBoxControl => NeverAskAgainBox;

    /// <summary>
    /// True after the first click on the delete button: the next click
    /// removes the marked entry.
    /// </summary>
    internal bool IsDeleteArmed => _deleteArmed;

    /// <summary>
    /// The window that changes the permissions, while it is open.
    /// </summary>
    internal SftpPermissionsWindow? PermissionsWindow => _permissionsWindow;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpPaneViewModel.SelectedRemoteEntry))
        {
            DisarmDelete();
            UpdateCommandState();
        }
    }

    /// <summary>
    /// The commands that work on an entry need a marked entry.
    /// </summary>
    private void UpdateCommandState()
    {
        var entry = _viewModel?.SelectedRemoteEntry;

        DeleteButton.IsEnabled = entry is not null;
        RenameButton.IsEnabled = entry is not null;
        ChmodButton.IsEnabled = entry is not null;
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.GoUpRemoteAsync();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.RefreshRemoteAsync();
        }
    }

    private void OnPathHomeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.GoHomeRemoteAsync();
        }
    }

    private async void OnRemoteListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        await viewModel.EnterRemoteAsync(entry);
    }

    private async void OnRemotePathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.RemotePath = RemotePathBox.Text ?? string.Empty;
        await viewModel.RefreshRemoteAsync();
    }

    private void OnSortNameClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Name);

    private void OnSortSizeClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Size);

    private void OnSortModifiedClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Modified);

    private void OnSortPermissionsClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Permissions);

    /// <summary>
    /// Puts the name of a new folder in front of the user.
    /// </summary>
    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        ShowPrompt(PromptMode.NewFolder, "新建文件夹", string.Empty);
    }

    /// <summary>
    /// Puts the name of the marked entry in front of the user, so that it
    /// can be changed.
    /// </summary>
    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        var entry = ViewModel?.SelectedRemoteEntry;
        if (entry is null)
        {
            return;
        }

        ShowPrompt(PromptMode.Rename, "重命名", entry.Name);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = ApplyPromptAsync();
        }
        else if (e.Key == Key.Escape)
        {
            HidePrompt();
        }
    }

    private void OnPromptOkClick(object? sender, RoutedEventArgs e)
    {
        _ = ApplyPromptAsync();
    }

    private void OnPromptCancelClick(object? sender, RoutedEventArgs e)
    {
        HidePrompt();
    }

    private async Task ApplyPromptAsync()
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var name = (PromptBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            PromptBox.Focus();
            return;
        }

        if (_promptMode == PromptMode.NewFolder)
        {
            HidePrompt();
            await viewModel.NewRemoteFolderAsync(name);
            return;
        }

        if (_promptMode == PromptMode.Rename)
        {
            var entry = viewModel.SelectedRemoteEntry;
            if (entry is null)
            {
                HidePrompt();
                return;
            }

            if (string.Equals(name, entry.Name, StringComparison.Ordinal))
            {
                // Nothing to rename to yet: keep the field in front.
                PromptBox.Focus();
                PromptBox.SelectAll();
                return;
            }

            HidePrompt();
            await viewModel.RenameRemoteAsync(name);
        }
    }

    private void ShowPrompt(PromptMode mode, string label, string text)
    {
        _promptMode = mode;
        PromptLabel.Text = label;
        PromptBox.Text = text;
        PromptRow.IsVisible = true;
        PromptBox.Focus();
        PromptBox.SelectAll();
    }

    private void HidePrompt()
    {
        _promptMode = PromptMode.None;
        PromptRow.IsVisible = false;
        PromptBox.Text = string.Empty;
    }

    /// <summary>
    /// The first click only asks, the second one deletes.
    /// </summary>
    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null || viewModel.SelectedRemoteEntry is null)
        {
            return;
        }

        if (!_deleteArmed)
        {
            ArmDelete();
            return;
        }

        DisarmDelete();
        await viewModel.DeleteRemoteAsync();
    }

    private void ArmDelete()
    {
        _deleteArmed = true;
        ToolTip.SetTip(DeleteButton, "再次点击确认删除");
        DeleteButton.Classes.Set("Armed", true);
    }

    private void DisarmDelete()
    {
        _deleteArmed = false;
        ToolTip.SetTip(DeleteButton, "删除");
        DeleteButton.Classes.Set("Armed", false);
    }

    private void OnChmodClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        var window = new SftpPermissionsWindow { Permission = entry.Permission };
        window.PermissionApplied += permission =>
        {
            _ = viewModel.SetRemotePermissionsAsync(permission.ToOctal());
        };

        _permissionsWindow = window;
        window.Closed += (_, _) => _permissionsWindow = null;
        window.Show();
    }

    /// <summary>
    /// Asks the system dialog for local files and sends them into the
    /// directory that the panel shows.
    /// </summary>
    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var pickers = Pickers ?? LocalPickers.Create(this);
        if (viewModel is null || pickers is null)
        {
            return;
        }

        var files = await pickers.PickFilesAsync("上传到当前文件夹");
        if (files.Count == 0)
        {
            return;
        }

        await viewModel.UploadAsync(files);
    }

    /// <summary>
    /// Asks the system dialog for a local directory and takes the marked
    /// entry into it.
    /// </summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var pickers = Pickers ?? LocalPickers.Create(this);
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || pickers is null || entry is null)
        {
            return;
        }

        var folder = await pickers.PickFolderAsync("下载到本地");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        await viewModel.DownloadAsync(folder);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelTransfer();
    }

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        await viewModel.EnsureLoadedAsync(CancellationToken.None);
    }

    private void OnConflictOverwriteClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Overwrite);
    }

    private void OnConflictSkipClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Skip);
    }

    private void OnConflictRenameClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Rename);
    }

    private void OnConflictCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Cancel);
    }
}
