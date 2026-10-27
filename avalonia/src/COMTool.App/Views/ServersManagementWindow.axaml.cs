using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.Servers;

namespace COMTool.App.Views;

/// <summary>
/// MobaXterm "Servers management" dialog (工具 → 网络服务器). Nine server
/// rows in MobaXterm order: FTP/Telnet/SSH-SFTP/Cron/Iperf run real
/// engines, NFS/VNC are placeholders that report in the server output.
/// Closing the dialog stops every running server.
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
        WireLog("tftp", TftpLogBox);
        WireLog("ftp", FtpLogBox);
        WireLog("http", HttpLogBox);
        WireLog("ssh", SshLogBox);
        WireLog("telnet", TelnetLogBox);
        WireLog("nfs", NfsLogBox);
        WireLog("vnc", VncLogBox);
        WireLog("cron", CronLogBox);
        WireLog("iperf", IperfLogBox);
    }

    internal ServersManagementViewModel ViewModel =>
        (ServersManagementViewModel)DataContext!;

    /// <summary>Test seam: id → host, replacing the real engines.</summary>
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

    private async void OnBrowseTftpRoot(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("选择 TFTP 服务器根目录");
        if (path is not null)
        {
            ViewModel.TftpRoot = path;
        }
    }

    private async void OnBrowseFtpRoot(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("选择 FTP 服务器根目录");
        if (path is not null)
        {
            ViewModel.FtpRoot = path;
        }
    }

    private async void OnBrowseHttpRoot(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("选择 HTTP 服务器根目录");
        if (path is not null)
        {
            ViewModel.HttpRoot = path;
        }
    }

    private void WireLog(string rowId, ListBox box)
    {
        if (ViewModel.Find(rowId) is { } row && box is not null)
        {
            row.Log.CollectionChanged += (_, _) => ScrollLogToEnd(box);
        }
    }

    /// <summary>MobaXterm keeps the newest log line in view.</summary>
    private static void ScrollLogToEnd(ListBox box)
    {
        if (box.ItemCount == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => box.ScrollIntoView(box.ItemCount - 1));
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
