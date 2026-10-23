using System.Collections.ObjectModel;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.Connections.Servers;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels.Tools;

public enum ServerConfigKind
{
    Unsupported,
    Http,
    Tftp,
}

/// <summary>One row of the MobaXterm "Servers management" list.</summary>
public sealed partial class ServerRowViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ServerConfigKind Kind { get; init; }

    public bool IsImplemented => Kind != ServerConfigKind.Unsupported;

    public string ToolTipText => IsImplemented ? "" : "暂不支持（框架已预留）";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int? _remainingSeconds;

    public ObservableCollection<string> Log { get; } = new();

    public string CountdownText => RemainingSeconds is int seconds ? $"({seconds})" : "";

    public bool ShowCountdown => IsRunning && RemainingSeconds is not null;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ShowCountdown));

    partial void OnRemainingSecondsChanged(int? value)
    {
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(ShowCountdown));
    }
}

/// <summary>
/// State and lifecycle of the "Servers management" dialog: nine MobaXterm
/// rows, HTTP/TFTP functional, the rest disabled placeholders. Owns one host
/// instance per running server, the runtime countdown timers and the logs.
/// </summary>
public sealed partial class ServersManagementViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private readonly AppConfigService? _config;
    private readonly Dictionary<string, IServerHost> _hosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DispatcherTimer> _timers = new(StringComparer.Ordinal);

    public ServersManagementViewModel(AppConfigService? config = null)
    {
        _config = config;
        Http = config?.Config.Servers.Http ?? new HttpServerSettings();
        Tftp = config?.Config.Servers.Tftp ?? new TftpServerSettings();

        Rows = new ObservableCollection<ServerRowViewModel>
        {
            new() { Id = "tftp", Name = "TFTP 服务器", Kind = ServerConfigKind.Tftp },
            new() { Id = "ftp", Name = "FTP 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "http", Name = "HTTP 服务器", Kind = ServerConfigKind.Http },
            new() { Id = "ssh", Name = "SSH/SFTP 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "telnet", Name = "Telnet 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "nfs", Name = "NFS 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "vnc", Name = "VNC 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "cron", Name = "Cron 服务器", Kind = ServerConfigKind.Unsupported },
            new() { Id = "iperf", Name = "Iperf 服务器", Kind = ServerConfigKind.Unsupported },
        };
    }

    public ObservableCollection<ServerRowViewModel> Rows { get; }

    public HttpServerSettings Http { get; }

    public TftpServerSettings Tftp { get; }

    /// <summary>Test seam: id → host, replacing the real HTTP/TFTP engines.</summary>
    public Func<string, IServerHost>? ServerFactory { get; set; }

    [ObservableProperty]
    private ServerRowViewModel? _selected;

    partial void OnSelectedChanged(ServerRowViewModel? value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsHttpSelected));
        OnPropertyChanged(nameof(IsTftpSelected));
    }

    public bool IsWelcome => Selected is null;

    public bool IsHttpSelected => Selected?.Kind == ServerConfigKind.Http;

    public bool IsTftpSelected => Selected?.Kind == ServerConfigKind.Tftp;

    public ServerRowViewModel? Find(string id) =>
        Rows.FirstOrDefault(row => row.Id == id);

    public ServerRowViewModel HttpRow => Find("http")!;

    public ServerRowViewModel TftpRow => Find("tftp")!;

    public ObservableCollection<string> HttpLog => HttpRow.Log;

    public ObservableCollection<string> TftpLog => TftpRow.Log;

    public bool HasRunningServers => _hosts.Count > 0;

    // --- UI proxy properties -------------------------------------------------
    // The settings objects are plain POCOs persisted in config.json; these
    // proxies give the dialog change notification and keep two-way bindings
    // simple.

    public string HttpRoot
    {
        get => Http.RootDirectory;
        set => SetHttp(() => Http.RootDirectory = value, Http.RootDirectory != value);
    }

    public bool HttpAllowListing
    {
        get => Http.AllowDirectoryListing;
        set => SetHttp(() => Http.AllowDirectoryListing = value,
            Http.AllowDirectoryListing != value);
    }

    public int HttpPort
    {
        get => Http.Port;
        set => SetHttp(() => Http.Port = value, Http.Port != value);
    }

    public bool HttpRuntimeEnabled
    {
        get => Http.EnableRuntimeLimit;
        set => SetHttp(() => Http.EnableRuntimeLimit = value,
            Http.EnableRuntimeLimit != value);
    }

    public int HttpRuntimeSeconds
    {
        get => Http.RuntimeSeconds;
        set => SetHttp(() => Http.RuntimeSeconds = value, Http.RuntimeSeconds != value);
    }

    public string TftpRoot
    {
        get => Tftp.RootDirectory;
        set => SetTftp(() => Tftp.RootDirectory = value, Tftp.RootDirectory != value);
    }

    public bool TftpShowDownload
    {
        get => Tftp.ShowDownloadInfo;
        set => SetTftp(() => Tftp.ShowDownloadInfo = value, Tftp.ShowDownloadInfo != value);
    }

    public bool TftpShowUpload
    {
        get => Tftp.ShowUploadInfo;
        set => SetTftp(() => Tftp.ShowUploadInfo = value, Tftp.ShowUploadInfo != value);
    }

    public int TftpPort
    {
        get => Tftp.Port;
        set => SetTftp(() => Tftp.Port = value, Tftp.Port != value);
    }

    public bool TftpRuntimeEnabled
    {
        get => Tftp.EnableRuntimeLimit;
        set => SetTftp(() => Tftp.EnableRuntimeLimit = value,
            Tftp.EnableRuntimeLimit != value);
    }

    public int TftpRuntimeSeconds
    {
        get => Tftp.RuntimeSeconds;
        set => SetTftp(() => Tftp.RuntimeSeconds = value, Tftp.RuntimeSeconds != value);
    }

    private void SetHttp(Action apply, bool changed, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!changed)
        {
            return;
        }
        apply();
        OnPropertyChanged(name);
    }

    private void SetTftp(Action apply, bool changed, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!changed)
        {
            return;
        }
        apply();
        OnPropertyChanged(name);
    }

    // --- Selection -----------------------------------------------------------

    /// <summary>Shows the row's configuration panel (⚙ and the row buttons).</summary>
    public void Select(ServerRowViewModel? row) =>
        Selected = row is { IsImplemented: true } ? row : null;

    // --- Lifecycle -----------------------------------------------------------

    public async Task StartAsync(ServerRowViewModel row)
    {
        if (!row.IsImplemented || row.IsRunning)
        {
            return;
        }

        Select(row);
        var root = RootFor(row.Kind);
        if (!Directory.Exists(root))
        {
            AppendLog(row, $"根目录不存在：{root}");
            return;
        }

        IServerHost host;
        try
        {
            host = ServerFactory?.Invoke(row.Id) ?? CreateHost(row.Kind, root);
            _hosts[row.Id] = host;
            host.LogMessage += message => Append(row, message);
            await host.StartAsync();
        }
        catch (Exception ex)
        {
            AppendLog(row, ex.Message);
            if (_hosts.Remove(row.Id, out var failed))
            {
                await failed.DisposeAsync();
            }
            row.IsRunning = false;
            return;
        }

        row.IsRunning = true;
        if (RuntimeEnabled(row.Kind))
        {
            row.RemainingSeconds = RuntimeSeconds(row.Kind);
            StartTimer(row);
        }

        SaveSettings();
    }

    public async Task StopAsync(ServerRowViewModel row)
    {
        StopTimer(row);
        row.RemainingSeconds = null;

        if (!_hosts.Remove(row.Id, out var host))
        {
            row.IsRunning = false;
            return;
        }

        try
        {
            await host.StopAsync();
        }
        catch (Exception ex)
        {
            AppendLog(row, ex.Message);
        }

        await host.DisposeAsync();
        row.IsRunning = false;
    }

    public async Task StopAllAsync()
    {
        foreach (var row in Rows.ToList())
        {
            if (_hosts.ContainsKey(row.Id))
            {
                await StopAsync(row);
            }
            else
            {
                StopTimer(row);
                row.IsRunning = false;
                row.RemainingSeconds = null;
            }
        }
    }

    public void SaveSettings() => _config?.Save();

    // --- Internals -----------------------------------------------------------

    private IServerHost CreateHost(ServerConfigKind kind, string root) => kind switch
    {
        ServerConfigKind.Http =>
            new HttpFileServer(root, Http.Port, Http.AllowDirectoryListing),
        ServerConfigKind.Tftp =>
            new TftpServer(root, Tftp.Port, Tftp.ShowDownloadInfo, Tftp.ShowUploadInfo),
        _ => throw new NotSupportedException("暂不支持（框架已预留）"),
    };

    private string RootFor(ServerConfigKind kind)
    {
        var configured = kind == ServerConfigKind.Http
            ? Http.RootDirectory
            : Tftp.RootDirectory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private bool RuntimeEnabled(ServerConfigKind kind) => kind switch
    {
        ServerConfigKind.Http => Http.EnableRuntimeLimit,
        ServerConfigKind.Tftp => Tftp.EnableRuntimeLimit,
        _ => false,
    };

    private int RuntimeSeconds(ServerConfigKind kind) => kind switch
    {
        ServerConfigKind.Http => Http.RuntimeSeconds,
        ServerConfigKind.Tftp => Tftp.RuntimeSeconds,
        _ => 0,
    };

    private void Append(ServerRowViewModel row, string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddLine(row, message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddLine(row, message));
        }
    }

    private void AppendLog(ServerRowViewModel row, string message) => Append(row, message);

    private static void AddLine(ServerRowViewModel row, string message)
    {
        row.Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (row.Log.Count > MaxLogLines)
        {
            row.Log.RemoveAt(0);
        }
    }

    private void StartTimer(ServerRowViewModel row)
    {
        StopTimer(row);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => OnTimerTick(row);
        _timers[row.Id] = timer;
        timer.Start();
    }

    private void StopTimer(ServerRowViewModel row)
    {
        if (_timers.Remove(row.Id, out var timer))
        {
            timer.Stop();
        }
    }

    private void OnTimerTick(ServerRowViewModel row)
    {
        if (row.RemainingSeconds is not int seconds)
        {
            StopTimer(row);
            return;
        }

        seconds--;
        if (seconds > 0)
        {
            row.RemainingSeconds = seconds;
            return;
        }

        StopTimer(row);
        row.RemainingSeconds = 0;
        AppendLog(row, "服务器运行时间到，服务器已停止");
        _ = StopAsync(row);
    }
}
