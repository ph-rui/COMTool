using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.Connections.Servers;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels.Tools;

public enum ServerConfigKind
{
    Tftp,
    Ftp,
    Http,
    Ssh,
    Telnet,
    Nfs,
    Vnc,
    Cron,
    Iperf,
}

/// <summary>One row of the MobaXterm "Servers management" list.</summary>
public sealed partial class ServerRowViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ServerConfigKind Kind { get; init; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isSelected;

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
/// rows, all selectable. FTP/Telnet/SSH-SFTP/Cron/Iperf are real engines,
/// NFS/VNC are placeholders that report their status in the server output.
/// Owns one host instance per running server, the runtime countdown timers
/// and the logs.
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
        var s = config?.Config.Servers;
        Http = s?.Http ?? new HttpServerSettings();
        Tftp = s?.Tftp ?? new TftpServerSettings();
        Ftp = s?.Ftp ?? new FtpServerSettings();
        Ssh = s?.Ssh ?? new SshServerSettings();
        Telnet = s?.Telnet ?? new TelnetServerSettings();
        Nfs = s?.Nfs ?? new NfsServerSettings();
        Vnc = s?.Vnc ?? new VncServerSettings();
        Cron = s?.Cron ?? new CronServerSettings();
        Iperf = s?.Iperf ?? new IperfServerSettings();

        if (string.IsNullOrEmpty(Ftp.Username))
        {
            Ftp.Username = Environment.UserName;
        }

        Rows = new ObservableCollection<ServerRowViewModel>
        {
            new() { Id = "tftp", Name = "TFTP 服务器", Kind = ServerConfigKind.Tftp },
            new() { Id = "ftp", Name = "FTP 服务器", Kind = ServerConfigKind.Ftp },
            new() { Id = "http", Name = "HTTP 服务器", Kind = ServerConfigKind.Http },
            new() { Id = "ssh", Name = "SSH/SFTP 服务器", Kind = ServerConfigKind.Ssh },
            new() { Id = "telnet", Name = "Telnet 服务器", Kind = ServerConfigKind.Telnet },
            new() { Id = "nfs", Name = "NFS 服务器", Kind = ServerConfigKind.Nfs },
            new() { Id = "vnc", Name = "VNC 服务器", Kind = ServerConfigKind.Vnc },
            new() { Id = "cron", Name = "Cron 服务器", Kind = ServerConfigKind.Cron },
            new() { Id = "iperf", Name = "Iperf 服务器", Kind = ServerConfigKind.Iperf },
        };

        // Adopt an already-running auto-started Cron server so its state is
        // reflected in the row and the dialog can stop it.
        if (CronAutoStart.Host is { IsRunning: true } sharedCron
            && Find("cron") is { } cronRow)
        {
            _hosts["cron"] = sharedCron;
            sharedCron.LogMessage += m => Append(cronRow, m);
            cronRow.IsRunning = true;
            if (Cron.EnableRuntimeLimit)
            {
                cronRow.RemainingSeconds = Cron.RuntimeSeconds;
                StartTimer(cronRow);
            }
        }
    }

    public ObservableCollection<ServerRowViewModel> Rows { get; }

    public HttpServerSettings Http { get; }

    public TftpServerSettings Tftp { get; }

    public FtpServerSettings Ftp { get; }

    public SshServerSettings Ssh { get; }

    public TelnetServerSettings Telnet { get; }

    public NfsServerSettings Nfs { get; }

    public VncServerSettings Vnc { get; }

    public CronServerSettings Cron { get; }

    public IperfServerSettings Iperf { get; }

    /// <summary>Test seam: id → host, replacing the real engines.</summary>
    public Func<string, IServerHost>? ServerFactory { get; set; }

    [ObservableProperty]
    private ServerRowViewModel? _selected;

    partial void OnSelectedChanged(ServerRowViewModel? value)
    {
        // Keep the row-level highlight flag in sync (drives the gear button
        // border); setting an equal value is a no-op in ObservableProperty.
        foreach (var row in Rows)
        {
            row.IsSelected = ReferenceEquals(row, value);
        }

        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsTftpSelected));
        OnPropertyChanged(nameof(IsFtpSelected));
        OnPropertyChanged(nameof(IsHttpSelected));
        OnPropertyChanged(nameof(IsSshSelected));
        OnPropertyChanged(nameof(IsTelnetSelected));
        OnPropertyChanged(nameof(IsNfsSelected));
        OnPropertyChanged(nameof(IsVncSelected));
        OnPropertyChanged(nameof(IsCronSelected));
        OnPropertyChanged(nameof(IsIperfSelected));
    }

    public bool IsWelcome => Selected is null;

    public bool IsTftpSelected => Selected?.Id == "tftp";
    public bool IsFtpSelected => Selected?.Id == "ftp";
    public bool IsHttpSelected => Selected?.Id == "http";
    public bool IsSshSelected => Selected?.Id == "ssh";
    public bool IsTelnetSelected => Selected?.Id == "telnet";
    public bool IsNfsSelected => Selected?.Id == "nfs";
    public bool IsVncSelected => Selected?.Id == "vnc";
    public bool IsCronSelected => Selected?.Id == "cron";
    public bool IsIperfSelected => Selected?.Id == "iperf";

    /// <summary>Test seam: true when the given row id's panel is the visible one.</summary>
    internal bool IsSelectedFor(string id) => Selected is not null && Selected.Id == id;

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
        set
        {
            if (Http.RootDirectory != value)
            {
                Http.RootDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HttpAllowListing
    {
        get => Http.AllowDirectoryListing;
        set
        {
            if (Http.AllowDirectoryListing != value)
            {
                Http.AllowDirectoryListing = value;
                OnPropertyChanged();
            }
        }
    }

    public int HttpPort
    {
        get => Http.Port;
        set
        {
            if (Http.Port != value)
            {
                Http.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HttpRuntimeEnabled
    {
        get => Http.EnableRuntimeLimit;
        set
        {
            if (Http.EnableRuntimeLimit != value)
            {
                Http.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int HttpRuntimeSeconds
    {
        get => Http.RuntimeSeconds;
        set
        {
            if (Http.RuntimeSeconds != value)
            {
                Http.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string TftpRoot
    {
        get => Tftp.RootDirectory;
        set
        {
            if (Tftp.RootDirectory != value)
            {
                Tftp.RootDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TftpShowDownload
    {
        get => Tftp.ShowDownloadInfo;
        set
        {
            if (Tftp.ShowDownloadInfo != value)
            {
                Tftp.ShowDownloadInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TftpShowUpload
    {
        get => Tftp.ShowUploadInfo;
        set
        {
            if (Tftp.ShowUploadInfo != value)
            {
                Tftp.ShowUploadInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public int TftpPort
    {
        get => Tftp.Port;
        set
        {
            if (Tftp.Port != value)
            {
                Tftp.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TftpRuntimeEnabled
    {
        get => Tftp.EnableRuntimeLimit;
        set
        {
            if (Tftp.EnableRuntimeLimit != value)
            {
                Tftp.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int TftpRuntimeSeconds
    {
        get => Tftp.RuntimeSeconds;
        set
        {
            if (Tftp.RuntimeSeconds != value)
            {
                Tftp.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string FtpRoot
    {
        get => Ftp.RootDirectory;
        set
        {
            if (Ftp.RootDirectory != value)
            {
                Ftp.RootDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public string FtpUser
    {
        get => Ftp.Username;
        set
        {
            if (Ftp.Username != value)
            {
                Ftp.Username = value;
                OnPropertyChanged();
            }
        }
    }

    public string FtpPassword
    {
        get => Ftp.Password;
        set
        {
            if (Ftp.Password != value)
            {
                Ftp.Password = value;
                OnPropertyChanged();
            }
        }
    }

    public bool FtpAnonymous
    {
        get => Ftp.AllowAnonymous;
        set
        {
            if (Ftp.AllowAnonymous != value)
            {
                Ftp.AllowAnonymous = value;
                OnPropertyChanged();
            }
        }
    }

    public bool FtpUtf8
    {
        get => Ftp.UseUtf8;
        set
        {
            if (Ftp.UseUtf8 != value)
            {
                Ftp.UseUtf8 = value;
                OnPropertyChanged();
            }
        }
    }

    public bool FtpPrompt
    {
        get => Ftp.PromptOnConnect;
        set
        {
            if (Ftp.PromptOnConnect != value)
            {
                Ftp.PromptOnConnect = value;
                OnPropertyChanged();
            }
        }
    }

    public int FtpPort
    {
        get => Ftp.Port;
        set
        {
            if (Ftp.Port != value)
            {
                Ftp.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool FtpRuntimeEnabled
    {
        get => Ftp.EnableRuntimeLimit;
        set
        {
            if (Ftp.EnableRuntimeLimit != value)
            {
                Ftp.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int FtpRuntimeSeconds
    {
        get => Ftp.RuntimeSeconds;
        set
        {
            if (Ftp.RuntimeSeconds != value)
            {
                Ftp.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string SshRoot
    {
        get => Ssh.RootDirectory;
        set
        {
            if (Ssh.RootDirectory != value)
            {
                Ssh.RootDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public int SshPort
    {
        get => Ssh.Port;
        set
        {
            if (Ssh.Port != value)
            {
                Ssh.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SshRuntimeEnabled
    {
        get => Ssh.EnableRuntimeLimit;
        set
        {
            if (Ssh.EnableRuntimeLimit != value)
            {
                Ssh.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int SshRuntimeSeconds
    {
        get => Ssh.RuntimeSeconds;
        set
        {
            if (Ssh.RuntimeSeconds != value)
            {
                Ssh.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public int TelnetPort
    {
        get => Telnet.Port;
        set
        {
            if (Telnet.Port != value)
            {
                Telnet.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public string TelnetPassword
    {
        get => Telnet.Password;
        set
        {
            if (Telnet.Password != value)
            {
                Telnet.Password = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TelnetPrompt
    {
        get => Telnet.PromptOnConnect;
        set
        {
            if (Telnet.PromptOnConnect != value)
            {
                Telnet.PromptOnConnect = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TelnetRuntimeEnabled
    {
        get => Telnet.EnableRuntimeLimit;
        set
        {
            if (Telnet.EnableRuntimeLimit != value)
            {
                Telnet.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int TelnetRuntimeSeconds
    {
        get => Telnet.RuntimeSeconds;
        set
        {
            if (Telnet.RuntimeSeconds != value)
            {
                Telnet.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string NfsRoot
    {
        get => Nfs.RootDirectory;
        set
        {
            if (Nfs.RootDirectory != value)
            {
                Nfs.RootDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public int NfsPort
    {
        get => Nfs.Port;
        set
        {
            if (Nfs.Port != value)
            {
                Nfs.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool NfsRuntimeEnabled
    {
        get => Nfs.EnableRuntimeLimit;
        set
        {
            if (Nfs.EnableRuntimeLimit != value)
            {
                Nfs.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int NfsRuntimeSeconds
    {
        get => Nfs.RuntimeSeconds;
        set
        {
            if (Nfs.RuntimeSeconds != value)
            {
                Nfs.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public int VncPort
    {
        get => Vnc.Port;
        set
        {
            if (Vnc.Port != value)
            {
                Vnc.Port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VncDescription));
                OnPropertyChanged(nameof(VncDescriptionHead));
            }
        }
    }

    public string VncPassword
    {
        get => Vnc.Password;
        set
        {
            if (Vnc.Password != value)
            {
                Vnc.Password = value;
                OnPropertyChanged();
            }
        }
    }

    public int VncHttpPort
    {
        get => Vnc.HttpPort;
        set
        {
            if (Vnc.HttpPort != value)
            {
                Vnc.HttpPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VncDescription));
                OnPropertyChanged(nameof(VncUrl));
            }
        }
    }

    public bool VncRuntimeEnabled
    {
        get => Vnc.EnableRuntimeLimit;
        set
        {
            if (Vnc.EnableRuntimeLimit != value)
            {
                Vnc.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int VncRuntimeSeconds
    {
        get => Vnc.RuntimeSeconds;
        set
        {
            if (Vnc.RuntimeSeconds != value)
            {
                Vnc.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string CronExpression
    {
        get => Cron.CronExpression;
        set
        {
            if (Cron.CronExpression != value)
            {
                Cron.CronExpression = value;
                OnPropertyChanged();
            }
        }
    }

    public string CronCommand
    {
        get => Cron.Command;
        set
        {
            if (Cron.Command != value)
            {
                Cron.Command = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CronStartWithApp
    {
        get => Cron.StartWithApp;
        set
        {
            if (Cron.StartWithApp != value)
            {
                Cron.StartWithApp = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CronRuntimeEnabled
    {
        get => Cron.EnableRuntimeLimit;
        set
        {
            if (Cron.EnableRuntimeLimit != value)
            {
                Cron.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int CronRuntimeSeconds
    {
        get => Cron.RuntimeSeconds;
        set
        {
            if (Cron.RuntimeSeconds != value)
            {
                Cron.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public int IperfPort
    {
        get => Iperf.Port;
        set
        {
            if (Iperf.Port != value)
            {
                Iperf.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IperfRuntimeEnabled
    {
        get => Iperf.EnableRuntimeLimit;
        set
        {
            if (Iperf.EnableRuntimeLimit != value)
            {
                Iperf.EnableRuntimeLimit = value;
                OnPropertyChanged();
            }
        }
    }

    public int IperfRuntimeSeconds
    {
        get => Iperf.RuntimeSeconds;
        set
        {
            if (Iperf.RuntimeSeconds != value)
            {
                Iperf.RuntimeSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    // --- Panel descriptions (MobaXterm 22.1 wording, product name adapted) ---

    /// <summary>SSH/SFTP panel intro (MobaXterm keeps the English intro; username is dynamic).</summary>
    public string SshDescription =>
        "This is a simple SSH/SFTP server: you can only log on using your current "
        + $"Windows login (\"{Environment.UserName}\") and the associated password.\n"
        + "如需完整的 Windows SSH 服务器，请下载 MobaSSH。";

    public string TelnetDescription =>
        "这是一个简单的 Telnet 服务器。\n"
        + "- 远程用户只能登录当前的 COMTool 会话\n"
        + "- 没有可用的高级选项";

    public string NfsDescription =>
        "这是一个基础的 NFS 服务器。\n"
        + "- 整个 \" 目录将被共享和导出\n"
        + "- 登录时无需提供用户或密码信息\n"
        + "- 远程用户将拥有与当前本地用户相同的权限\n"
        + "- 没有可用的高级选项";

    /// <summary>VNC panel intro (lines 1-2, above the URL line); port is dynamic.</summary>
    public string VncDescriptionHead =>
        "这是一个基于 TightVNC 服务器的简单 VNC 服务器。\n"
        + $"- VNC 请求可被发送到 {Vnc.Port} 端口";

    /// <summary>VNC panel intro line 3 (the browser URL); local IP and HTTP port dynamic.</summary>
    public string VncUrl => "http://" + GetLocalIp() + ":" + Vnc.HttpPort;

    /// <summary>Full 3-line intro (used by tests); panel splits it into Head + linked URL.</summary>
    public string VncDescription =>
        VncDescriptionHead + "\n- 您可以使用浏览器打开地址浏览: " + VncUrl;

    public string CronDescription =>
        "这是一个用于 COMTool 的简单 Cron 服务器。\n"
        + "- COMTool 停止时，该服务器无法运行\n"
        + "- 定时表达式与执行命令在下方编辑";

    public string IperfDescription =>
        "这是一个用于 COMTool 的简单 Iperf 服务器。\n"
        + "您可通过运行以下命令来测试另一台电脑和此电脑之间的带宽:\n"
        + "iperf -c <MyComputerIpAddress>";

    /// <summary>First non-loopback IPv4 address (MobaXterm shows the LAN IP).</summary>
    internal static string GetLocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up
                    || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(ua.Address))
                    {
                        return ua.Address.ToString();
                    }
                }
            }
        }
        catch (Exception)
        {
            // fall through to the loopback fallback
        }

        return "127.0.0.1";
    }

    // --- Selection -----------------------------------------------------------

    /// <summary>Shows the row's configuration panel (⚙ and the row buttons).</summary>
    public void Select(ServerRowViewModel? row) => Selected = row;

    // --- Lifecycle -----------------------------------------------------------

    public async Task StartAsync(ServerRowViewModel row)
    {
        if (row.IsRunning)
        {
            return;
        }

        Select(row);
        var kind = row.Kind;
        if (KindHasRoot(kind))
        {
            var root = RootFor(kind);
            if (!Directory.Exists(root))
            {
                AppendLog(row, $"根目录不存在：{root}");
                return;
            }
        }

        IServerHost host;
        try
        {
            host = ServerFactory?.Invoke(row.Id) ?? CreateHost(kind);
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

        // Placeholder engines (NFS/VNC) and a missing iperf binary report
        // their status in the log and stay unstarted.
        row.IsRunning = host.IsRunning;
        if (row.IsRunning && RuntimeEnabled(kind))
        {
            row.RemainingSeconds = RuntimeSeconds(kind);
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
        if (host is CronServer cronHost)
        {
            CronAutoStart.Detach(cronHost);
        }
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

    private IServerHost CreateHost(ServerConfigKind kind)
    {
        switch (kind)
        {
            case ServerConfigKind.Tftp:
                return new TftpServer(RootFor(kind), Tftp.Port,
                    Tftp.ShowDownloadInfo, Tftp.ShowUploadInfo);
            case ServerConfigKind.Ftp:
                return new FtpServer(RootFor(kind), Ftp.Port,
                    Ftp.AllowAnonymous, Ftp.Username, Ftp.Password);
            case ServerConfigKind.Http:
                return new HttpFileServer(RootFor(kind), Http.Port, Http.AllowDirectoryListing);
            case ServerConfigKind.Ssh:
                return new SshServerHost(RootFor(kind), Ssh.Port);
            case ServerConfigKind.Telnet:
                return new TelnetServer(Telnet.Port);
            case ServerConfigKind.Nfs:
                return new StubServer(
                    "NFS 服务器",
                    "NFS 服务器暂未实现：真实 NFS 服务需要系统级组件（portmapper/mountd/NFSd），本版本仅预留界面");
            case ServerConfigKind.Vnc:
                return new StubServer(
                    "VNC 服务器",
                    "VNC 服务器暂未实现：需要 RFB 协议与虚拟画面，本版本仅预留界面");
            case ServerConfigKind.Cron:
                {
                    var cron = new CronServer(Cron.CronExpression, Cron.Command);
                    CronAutoStart.Adopt(cron);
                    return cron;
                }
            case ServerConfigKind.Iperf:
                return new IperfServer(Iperf.Port);
            default:
                throw new NotSupportedException($"未知的服务器类型: {kind}");
        }
    }

    private static bool KindHasRoot(ServerConfigKind kind) => kind is
        ServerConfigKind.Tftp or ServerConfigKind.Ftp
        or ServerConfigKind.Http or ServerConfigKind.Ssh;

    private string RootFor(ServerConfigKind kind)
    {
        string? configured = kind switch
        {
            ServerConfigKind.Http => Http.RootDirectory,
            ServerConfigKind.Tftp => Tftp.RootDirectory,
            ServerConfigKind.Ftp => Ftp.RootDirectory,
            ServerConfigKind.Ssh => Ssh.RootDirectory,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        // MobaXterm 的 SSH/SFTP 服务器以当前用户主目录为根；其余服务器用文档目录。
        if (kind == ServerConfigKind.Ssh)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private bool RuntimeEnabled(ServerConfigKind kind) => kind switch
    {
        ServerConfigKind.Http => Http.EnableRuntimeLimit,
        ServerConfigKind.Tftp => Tftp.EnableRuntimeLimit,
        ServerConfigKind.Ftp => Ftp.EnableRuntimeLimit,
        ServerConfigKind.Ssh => Ssh.EnableRuntimeLimit,
        ServerConfigKind.Telnet => Telnet.EnableRuntimeLimit,
        ServerConfigKind.Iperf => Iperf.EnableRuntimeLimit,
        ServerConfigKind.Nfs => Nfs.EnableRuntimeLimit,
        ServerConfigKind.Vnc => Vnc.EnableRuntimeLimit,
        ServerConfigKind.Cron => Cron.EnableRuntimeLimit,
        _ => false,
    };

    private int RuntimeSeconds(ServerConfigKind kind) => kind switch
    {
        ServerConfigKind.Http => Http.RuntimeSeconds,
        ServerConfigKind.Tftp => Tftp.RuntimeSeconds,
        ServerConfigKind.Ftp => Ftp.RuntimeSeconds,
        ServerConfigKind.Ssh => Ssh.RuntimeSeconds,
        ServerConfigKind.Telnet => Telnet.RuntimeSeconds,
        ServerConfigKind.Iperf => Iperf.RuntimeSeconds,
        ServerConfigKind.Nfs => Nfs.RuntimeSeconds,
        ServerConfigKind.Vnc => Vnc.RuntimeSeconds,
        ServerConfigKind.Cron => Cron.RuntimeSeconds,
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
