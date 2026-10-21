using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using COMTool.App.ViewModels;

namespace COMTool.App.Views;

/// <summary>
/// The drawer of an SSH session: the remote side on the left, the local
/// side on the right, a toolbar above and the state of the transfer
/// below.
/// </summary>
public sealed partial class SftpPane : UserControl
{
    private SftpPaneViewModel? _viewModel;

    public SftpPane()
    {
        InitializeComponent();
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
            _viewModel = value;
            DataContext = value;
        }
    }

    internal ListBox RemoteListControl => RemoteList;

    internal ListBox LocalListControl => LocalList;

    internal TextBox RemotePathBoxControl => RemotePathBox;

    internal TextBox LocalPathBoxControl => LocalPathBox;

    internal Button UpButtonControl => UpButton;

    internal Button RefreshButtonControl => RefreshButton;

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

    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnChmodClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnUploadClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelTransfer();
    }
}
