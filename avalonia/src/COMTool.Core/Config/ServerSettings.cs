using System.Text.Json.Serialization;

namespace COMTool.Core.Config;

/// <summary>
/// Settings of the "Servers management" dialog (MobaXterm 网络服务器). Only
/// the two functional servers are persisted; the other rows are display-only
/// placeholders.
/// </summary>
public sealed class ServerSettings
{
    [JsonPropertyName("http")]
    public HttpServerSettings Http { get; set; } = new();

    [JsonPropertyName("tftp")]
    public TftpServerSettings Tftp { get; set; } = new();
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
