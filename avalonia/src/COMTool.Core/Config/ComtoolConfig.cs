using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// Recently opened saved sessions, most recent first. Only kind and id
    /// are stored; the display data is resolved from the session stores so a
    /// removed session never leaves a stale clickable row behind.
    /// </summary>
    [JsonPropertyName("recentSessions")]
    public List<RecentSessionConfig> RecentSessions { get; set; } = new();

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
    public int Width { get; set; } = 200;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MacroConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("appendEnter")]
    public bool AppendEnter { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class RecentSessionConfig
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("at")]
    public long At { get; set; }

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
