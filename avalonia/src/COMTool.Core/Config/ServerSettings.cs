using System.Text.Json.Serialization;

namespace COMTool.Core.Config;

/// <summary>
/// Settings of the "Servers management" dialog (MobaXterm 网络服务器).
/// Each row owns a plain POCO; values survive restarts in config.json.
/// </summary>
public sealed class ServerSettings
{
    [JsonPropertyName("http")]
    public HttpServerSettings Http { get; set; } = new();

    [JsonPropertyName("tftp")]
    public TftpServerSettings Tftp { get; set; } = new();

    [JsonPropertyName("ftp")]
    public FtpServerSettings Ftp { get; set; } = new();

    [JsonPropertyName("ssh")]
    public SshServerSettings Ssh { get; set; } = new();

    [JsonPropertyName("telnet")]
    public TelnetServerSettings Telnet { get; set; } = new();

    [JsonPropertyName("nfs")]
    public NfsServerSettings Nfs { get; set; } = new();

    [JsonPropertyName("vnc")]
    public VncServerSettings Vnc { get; set; } = new();

    [JsonPropertyName("cron")]
    public CronServerSettings Cron { get; set; } = new();

    [JsonPropertyName("iperf")]
    public IperfServerSettings Iperf { get; set; } = new();
}

public sealed class HttpServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 80;

    /// <summary>Empty means the user's Documents folder (MobaXterm default).</summary>
    [JsonPropertyName("rootDirectory")]
    public string RootDirectory { get; set; } = "";

    [JsonPropertyName("allowDirectoryListing")]
    public bool AllowDirectoryListing { get; set; } = true;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class TftpServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 69;

    /// <summary>Empty means the user's Documents folder (MobaXterm default).</summary>
    [JsonPropertyName("rootDirectory")]
    public string RootDirectory { get; set; } = "";

    [JsonPropertyName("showDownloadInfo")]
    public bool ShowDownloadInfo { get; set; } = true;

    [JsonPropertyName("showUploadInfo")]
    public bool ShowUploadInfo { get; set; } = true;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class FtpServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 21;

    /// <summary>Empty means the user's Documents folder (MobaXterm default).</summary>
    [JsonPropertyName("rootDirectory")]
    public string RootDirectory { get; set; } = "";

    /// <summary>Account shown in the 用户 field; empty means the current Windows user.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    /// <summary>When true any credentials log in (MobaXterm default: checked).</summary>
    [JsonPropertyName("allowAnonymous")]
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>使用 UTF-8 字节集 (UI option; the server always exchanges UTF-8 paths).</summary>
    [JsonPropertyName("useUtf8")]
    public bool UseUtf8 { get; set; } = false;

    /// <summary>在接受任何传入的连接前提示我 (UI option; connections are logged instead).</summary>
    [JsonPropertyName("promptOnConnect")]
    public bool PromptOnConnect { get; set; } = true;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class SshServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 22;

    /// <summary>
    /// SFTP root of the built-in server. Empty means the user's Documents
    /// folder (MobaXterm default).
    /// </summary>
    [JsonPropertyName("rootDirectory")]
    public string RootDirectory { get; set; } = "";

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class TelnetServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 23;

    /// <summary>UI option; the built-in echo engine does not challenge clients.</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("promptOnConnect")]
    public bool PromptOnConnect { get; set; } = true;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

/// <summary>Placeholder row: a real NFS server needs system-level services.</summary>
public sealed class NfsServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 2049;

    /// <summary>Empty means the user's Documents folder (MobaXterm default).</summary>
    [JsonPropertyName("rootDirectory")]
    public string RootDirectory { get; set; } = "";

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

/// <summary>Placeholder row: a full VNC server is not implemented yet.</summary>
public sealed class VncServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 5900;

    /// <summary>UI option; the placeholder engine does not open a display.</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    /// <summary>Browser port shown in the panel description (MobaXterm default 5800).</summary>
    [JsonPropertyName("httpPort")]
    public int HttpPort { get; set; } = 5800;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class CronServerSettings
{
    /// <summary>5-field cron expression, e.g. "*/5 * * * *" (every 5 min).</summary>
    [JsonPropertyName("cronExpression")]
    public string CronExpression { get; set; } = "*/5 * * * *";

    /// <summary>Command executed on every matched minute (empty = idle).</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>"COMTool 启动时启动 Cron 服务器" — start the cron server with the app.</summary>
    [JsonPropertyName("startWithApp")]
    public bool StartWithApp { get; set; } = false;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}

public sealed class IperfServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 5201;

    [JsonPropertyName("enableRuntimeLimit")]
    public bool EnableRuntimeLimit { get; set; } = true;

    [JsonPropertyName("runtimeSeconds")]
    public int RuntimeSeconds { get; set; } = 3600;
}
