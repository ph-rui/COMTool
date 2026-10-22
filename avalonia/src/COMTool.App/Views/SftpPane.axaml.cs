using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using COMTool.App.ViewModels;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// The drawer of an SSH session: the remote side on the left, the local
/// side on the right, a toolbar above and the state of the transfer
/// below.
/// </summary>
public sealed partial class SftpPane : UserControl
{
    private const string ConfirmDeleteText = "Confirm delete";

    private SftpPaneViewModel? _viewModel;
    private SftpPermissionsWindow? _permissionsWindow;
    private string? _lastSelectedRemotePath;

    public SftpPane()
    {
        InitializeComponent();

        UpdateCommandState();
    }

    /// <summary>
    /// The state of the drawer. It becomes the context of the view, so
    /// that every binding inside the drawer finds it.
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

            _lastSelectedRemotePath = null;
            UpdateCommandState();
        }
    }

    internal ListBox RemoteListControl => RemoteList;

    internal ListBox LocalListControl => LocalList;

    internal TextBox RemotePathBoxControl => RemotePathBox;

    internal TextBox LocalPathBoxControl => LocalPathBox;

    internal Button UpButtonControl => UpButton;

    internal Button RefreshButtonControl => RefreshButton;

    internal Button NewFolderButtonControl => NewFolderButton;

    internal Button RenameButtonControl => RenameButton;

    internal Button DeleteButtonControl => DeleteButton;

    internal Button ChmodButtonControl => ChmodButton;

    internal TextBox NewFolderNameBoxControl => NewFolderNameBox;

    internal TextBox RenameNameBoxControl => RenameNameBox;

    internal Grid RemotePaneGridControl => RemotePaneGrid;

    internal Grid LocalPaneGridControl => LocalPaneGrid;

    internal Button UploadButtonControl => UploadButton;

    internal Button DownloadButtonControl => DownloadButton;

    internal Button CancelButtonControl => CancelButton;

    internal Button RetryButtonControl => RetryButton;

    internal ProgressBar TransferProgressBarControl => TransferProgressBar;

    internal Border ConflictPanelControl => ConflictPanel;

    internal TextBox ConflictRenameBoxControl => ConflictRenameBox;

    internal CheckBox ApplyToAllBoxControl => ApplyToAllBox;

    internal CheckBox NeverAskAgainBoxControl => NeverAskAgainBox;

    /// <summary>
    /// The window that changes the permissions, while it is open.
    /// </summary>
    internal SftpPermissionsWindow? PermissionsWindow => _permissionsWindow;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpPaneViewModel.SelectedRemoteEntry))
        {
            UpdateCommandState();
        }
    }

    /// <summary>
    /// The commands that work on the remote side need a marked entry. The
    /// new folder needs a name in its field.
    /// </summary>
    private void UpdateCommandState()
    {
        var entry = _viewModel?.SelectedRemoteEntry;

        DeleteButton.IsEnabled = entry is not null;
        RenameButton.IsEnabled = entry is not null;
        ChmodButton.IsEnabled = entry is not null;
        NewFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(NewFolderNameBox.Text);

        if (entry is null)
        {
            // A delete that waits for a second click does not wait
            // forever.
            DeleteButton.Content = "Delete";
            _lastSelectedRemotePath = null;
            return;
        }

        if (string.Equals(_lastSelectedRemotePath, entry.Path, StringComparison.Ordinal))
        {
            return;
        }

        // The field of the rename follows the entry that was marked.
        _lastSelectedRemotePath = entry.Path;
        RenameNameBox.Text = entry.Name;
    }

    private async void OnUpClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        // The pane that was worked on last is the one that goes up. The
        // remote side is the one that counts when both are marked.
        if (viewModel.SelectedLocalEntry is not null && viewModel.SelectedRemoteEntry is null)
        {
            await viewModel.GoUpLocalAsync();
            return;
        }

        await viewModel.GoUpRemoteAsync();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        await viewModel.RefreshRemoteAsync();
        await viewModel.RefreshLocalAsync();
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

    private async void OnLocalListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedLocalEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        await viewModel.EnterLocalAsync(entry);
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

    private async void OnLocalPathKeyDown(object? sender, KeyEventArgs e)
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

        viewModel.LocalPath = LocalPathBox.Text ?? string.Empty;
        await viewModel.RefreshLocalAsync();
    }

    private void OnNewFolderNameChanged(object? sender, TextChangedEventArgs e)
    {
        NewFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(NewFolderNameBox.Text);
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var name = NewFolderNameBox.Text;
        if (viewModel is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await viewModel.NewRemoteFolderAsync(name.Trim());
        NewFolderNameBox.Text = string.Empty;
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        await RenameSelectedRemoteAsync();
    }

    private async void OnRenameNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await RenameSelectedRemoteAsync();
    }

    /// <summary>
    /// Renames the marked remote entry to the name in the field. As long
    /// as the name is the name of the entry, the field is only put in
    /// front of the user, so that the name can be changed.
    /// </summary>
    private async Task RenameSelectedRemoteAsync()
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        var name = (RenameNameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            RenameNameBox.Text = entry.Name;
            return;
        }

        if (string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            RenameNameBox.Focus();
            RenameNameBox.SelectAll();
            return;
        }

        await viewModel.RenameRemoteAsync(name);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null || viewModel.SelectedRemoteEntry is null)
        {
            return;
        }

        if (sender is not Button button)
        {
            return;
        }

        if (!string.Equals(button.Content as string, ConfirmDeleteText, StringComparison.Ordinal))
        {
            // The first click only asks, the second one deletes.
            button.Content = ConfirmDeleteText;
            return;
        }

        button.Content = "Delete";
        await viewModel.DeleteRemoteAsync();
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

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        await viewModel.UploadAsync();
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        await viewModel.DownloadAsync();
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
