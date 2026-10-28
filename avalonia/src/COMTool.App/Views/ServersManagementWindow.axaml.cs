using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
/// Closing the dialog does NOT stop the running servers (MobaXterm parity):
/// hosts/logs/countdowns live in the process-wide ServersSession and are
/// restored when the window is reopened; the app exit stops them all
/// (MainWindow.OnClosed → ServersSession.Shared.StopAllAsync).
/// </summary>
public partial class ServersManagementWindow : Window
{
    public ServersManagementWindow()
        : this((AppConfigService?)null)
    {
    }

    public ServersManagementWindow(AppConfigService? config)
    {
        InitializeComponent();
        DataContext = ServersSession.GetOrCreate(config);
        InitLogWiring();
    }

    /// <summary>测试入口：使用外部提供的 ViewModel，不经过进程级共享会话。</summary>
    internal ServersManagementWindow(ServersManagementViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        InitLogWiring();
    }

    private void InitLogWiring()
    {
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

    // 日志滚动订阅。VM 现为进程级共享（ServersSession），必须随关窗解除，
    // 否则已关闭窗口的 ListBox 会被静态 VM 永久引用（每次重开漏一个窗口）。
    private readonly List<NotifyCollectionChangedEventHandler> _logHandlers = new();
    private readonly List<ObservableCollection<string>> _logSources = new();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 共享 VM（ServersSession）比窗口活得长：解除日志滚动订阅，
        // 避免已关闭窗口的 ListBox 被静态 VM 永久引用。
        for (var i = 0; i < _logHandlers.Count; i++)
        {
            _logSources[i].CollectionChanged -= _logHandlers[i];
        }
        _logHandlers.Clear();
        _logSources.Clear();

        // 对齐 MobaXterm：关对话框不停服务——宿主/日志/倒计时由进程级
        // ServersSession 持有，重开窗口即恢复；应用退出时由 MainWindow.OnClosed
        // 统一 StopAllAsync。这里只保存设置。
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
            var handler = new NotifyCollectionChangedEventHandler((_, _) => ScrollLogToEnd(box));
            row.Log.CollectionChanged += handler;
            _logHandlers.Add(handler);
            _logSources.Add(row.Log);
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
