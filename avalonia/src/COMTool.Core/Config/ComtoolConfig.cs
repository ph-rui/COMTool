using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using COMTool.Core.Macros;

namespace COMTool.Core.Config;

public sealed class ComtoolConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("skin")]
    public string Skin { get; set; } = "light";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "en";

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "UTF-8";

    [JsonPropertyName("skipVersion")]
    public string? SkipVersion { get; set; }

    [JsonPropertyName("connId")]
    public string ConnId { get; set; } = "serial";

    [JsonPropertyName("pluginsInfo")]
    public JsonElement? PluginsInfo { get; set; }

    [JsonPropertyName("activeItem")]
    public string? ActiveItem { get; set; }

    [JsonPropertyName("currItem")]
    public string? CurrentItem { get; set; }

    [JsonPropertyName("serialSessions")]
    public List<SerialSession> SerialSessions { get; set; } = new();

    [JsonPropertyName("serialSessionsMigrated")]
    public bool SerialSessionsMigrated { get; set; }

    [JsonPropertyName("sshSessions")]
    public List<SshSession> SshSessions { get; set; } = new();

    [JsonPropertyName("sshSessionsMigrated")]
    public bool SshSessionsMigrated { get; set; }

    [JsonPropertyName("telnetSessions")]
    public List<TelnetSession> TelnetSessions { get; set; } = new();

    [JsonPropertyName("tcpSessions")]
    public List<TcpSession> TcpSessions { get; set; } = new();

    [JsonPropertyName("sessionSidebar")]
    public SessionSidebarConfig SessionSidebar { get; set; } = new();

    /// <summary>
    /// Settings of the tools "网络服务器" dialog (HTTP/TFTP servers). Missing
    /// from older config files, in which case the defaults apply.
    /// </summary>
    [JsonPropertyName("servers")]
    public ServerSettings Servers { get; set; } = new();

    /// <summary>
    /// The user's global macro list shown in the sidebar Macros tab. Macros
    /// are optional: configs written before the tab existed simply load an
    /// empty list.
    /// </summary>
    [JsonPropertyName("macros")]
    public List<MacroConfig> Macros { get; set; } = new();

    /// <summary>
    /// Remembered 网络扫描仪 "IP 地址范围" (MobaXterm keeps the last range
    /// between dialog invocations). Missing from older config files, in which
    /// case the 192.168.1.1→254 default applies.
    /// </summary>
    [JsonPropertyName("networkScan")]
    public NetworkScanConfig NetworkScan { get; set; } = new();

    /// <summary>
    /// Remembered 端口扫描仪 inputs: the last target IP and the port range
    /// used in "扫描端口范围" mode. Missing from older config files, in which
    /// case the 127.0.0.1 / 1→65535 defaults apply.
    /// </summary>
    [JsonPropertyName("portScan")]
    public PortScanConfig PortScan { get; set; } = new();

    /// <summary>
    /// 远程监控状态栏菜单 11 项勾选状态。旧配置缺失该键时
    /// 取 MobaXterm 22.1 默认（7 项开启）。
    /// </summary>
    [JsonPropertyName("remoteMonitor")]
    public RemoteMonitorConfig RemoteMonitor { get; set; } = new();

    [JsonPropertyName("window")]
    public WindowConfig Window { get; set; } = new();

    [JsonPropertyName("items")]
    public List<JsonElement> Items { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static ComtoolConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ComtoolConfig();
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ComtoolConfig>(json) ?? new ComtoolConfig();
        if (config.Version != 3)
        {
            var backup = $"{path}.bak.{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";
            File.Copy(path, backup, overwrite: false);
            return new ComtoolConfig();
        }

        return config;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }
}

public sealed class SessionSidebarConfig
{
    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 260;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MacroConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Step-based definition (MobaXterm-style). When empty and <see cref="Text"/>
    /// is set, the legacy single-text format is migrated on load.
    /// </summary>
    [JsonPropertyName("steps")]
    public List<MacroStepDto> Steps { get; set; } = new();

    /// <summary>Global hotkey, e.g. "Ctrl+Shift+M". Null/empty = none.</summary>
    [JsonPropertyName("hotkey")]
    public string? Hotkey { get; set; }

    // Legacy fields, kept so pre-migration configs keep deserializing.
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("appendEnter")]
    public bool AppendEnter { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class WindowConfig
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 850;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 500;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SerialSession
{
    [JsonPropertyName("port")]
    public string Port { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("serial")]
    public SerialConnectionConfig Serial { get; set; } = new();

    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SerialConnectionConfig
{
    [JsonPropertyName("port")]
    public string Port { get; set; } = "";

    [JsonPropertyName("baudrate")]
    public int Baudrate { get; set; } = 115200;

    [JsonPropertyName("bytesize")]
    public int Bytesize { get; set; } = 8;

    [JsonPropertyName("parity")]
    public string Parity { get; set; } = "None";

    [JsonPropertyName("stopbits")]
    public string Stopbits { get; set; } = "1";

    [JsonPropertyName("flowcontrol")]
    public string Flowcontrol { get; set; } = "None";

    [JsonPropertyName("rts")]
    public bool Rts { get; set; }

    [JsonPropertyName("dtr")]
    public bool Dtr { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class TcpSession
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "tcp";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "client";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 2345;

    [JsonPropertyName("auto_reconnect")]
    public bool AutoReconnect { get; set; }

    [JsonPropertyName("auto_reconnect_interval")]
    public double ReconnectInterval { get; set; } = 1.0;

    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SshSession
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 22;

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = "";

    [JsonPropertyName("ssh_key")]
    public string SshKey { get; set; } = "";

    [JsonPropertyName("passwd_enc")]
    public string EncryptedPassword { get; set; } = "";

    /// <summary>
    /// 该会话的远程监控开关（MobaXterm 式记忆：再次打开该会话时自动恢复）。
    /// 旧配置缺此键时反序列化为 false。
    /// </summary>
    [JsonPropertyName("monitor")]
    public bool Monitor { get; set; }

    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class TelnetSession
{
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "telnet";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 23;

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = "";

    [JsonPropertyName("passwd_enc")]
    public string EncryptedPassword { get; set; } = "";

    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// 网络扫描仪 remembered "IP 地址范围" form: three fixed octets plus the
/// last-octet range (MobaXterm scans a single /24). Kept as strings so the
/// dialog restores exactly what the user last used.
/// </summary>
public sealed class NetworkScanConfig
{
    [JsonPropertyName("octet1")]
    public string Octet1 { get; set; } = "192";

    [JsonPropertyName("octet2")]
    public string Octet2 { get; set; } = "168";

    [JsonPropertyName("octet3")]
    public string Octet3 { get; set; } = "1";

    [JsonPropertyName("from")]
    public string From { get; set; } = "1";

    [JsonPropertyName("to")]
    public string To { get; set; } = "254";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// 端口扫描仪 remembered inputs: the target IP and the explicit port range
/// (used when "扫描端口范围" is selected). Kept as strings so the dialog
/// restores exactly what the user last used.
/// </summary>
public sealed class PortScanConfig
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "127.0.0.1";

    [JsonPropertyName("from")]
    public string From { get; set; } = "1";

    [JsonPropertyName("to")]
    public string To { get; set; } = "65535";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// 远程监控（remote monitoring）状态栏右键菜单的 11 项开关。
/// 默认与 MobaXterm 22.1 一致：主机/CPU/RAM/网络上传/网络下载/
/// 正常运行时间/分区信息 7 项开启，其余 4 项关闭。
/// </summary>
public sealed class RemoteMonitorConfig
{
    [JsonPropertyName("show_host")]
    public bool ShowHost { get; set; } = true;

    [JsonPropertyName("show_cpu")]
    public bool ShowCpu { get; set; } = true;

    [JsonPropertyName("show_ram")]
    public bool ShowRam { get; set; } = true;

    [JsonPropertyName("show_net_up")]
    public bool ShowNetUp { get; set; } = true;

    [JsonPropertyName("show_net_down")]
    public bool ShowNetDown { get; set; } = true;

    [JsonPropertyName("show_clients")]
    public bool ShowClients { get; set; }

    [JsonPropertyName("show_processes")]
    public bool ShowProcesses { get; set; }

    [JsonPropertyName("show_file_descriptors")]
    public bool ShowFileDescriptors { get; set; }

    [JsonPropertyName("show_uptime")]
    public bool ShowUptime { get; set; } = true;

    [JsonPropertyName("show_users")]
    public bool ShowUsers { get; set; }

    [JsonPropertyName("show_partitions")]
    public bool ShowPartitions { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
