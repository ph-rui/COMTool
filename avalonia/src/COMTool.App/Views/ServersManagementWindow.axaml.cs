using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using COMTool.App.Services;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.Servers;

namespace COMTool.App.Views;

/// <summary>
/// MobaXterm "Servers management" dialog (工具 → 网络服务器). Nine server rows,
/// HTTP and TFTP functional, the rest disabled placeholders. Closing the
/// dialog stops every running server.
/// </summary>
public partial class ServersManagementWindow : Window
{
    private readonly AppConfigService? _config;

    public ServersManagementWindow()
        : this(null)
    {
    }

    public ServersManagementWindow(AppConfigService? config)
    {
        InitializeComponent();
        _config = config;
        DataContext = new ServersManagementViewModel(config);
    }

    internal ServersManagementViewModel ViewModel =>
        (ServersManagementViewModel)DataContext!;

    /// <summary>Test seam: id → host, replacing the real HTTP/TFTP engines.</summary>
    internal Func<string, IServerHost>? ServerFactoryForTests
    {
        get => ViewModel.ServerFactory;
        set => ViewModel.ServerFactory = value;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Listener shutdown happens synchronously at the start of StopAsync,
        // so the sockets are already closed when the window disappears.
        _ = ViewModel.StopAllAsync();
        ViewModel.SaveSettings();
        base.OnClosing(e);
    }

    private async void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            await ViewModel.StartAsync(row);
        }
    }

    private async void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            await ViewModel.StopAsync(row);
        }
    }

    private void OnGearClick(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.Select(row);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseHttpRoot(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("选择 HTTP 服务器根目录");
        if (path is not null)
        {
            ViewModel.HttpRoot = path;
        }
    }

    private async void OnBrowseTftpRoot(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("选择 TFTP 服务器根目录");
        if (path is not null)
        {
            ViewModel.TftpRoot = path;
        }
    }

    private static ServerRowViewModel? RowOf(object? sender) =>
        (sender as Control)?.DataContext as ServerRowViewModel;

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
