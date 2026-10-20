using System.Net;

namespace COMTool.Connections;

public sealed record TcpClientInfo(string Endpoint);

public sealed record TcpClientStatusEventArgs(string Message, bool IsConnected);

public sealed record TcpClientOptions(
    string Host,
    int Port,
    TimeSpan? Timeout = null,
    bool AutoReconnect = false,
    TimeSpan? ReconnectInterval = null);

public sealed record TcpServerOptions(
    int Port,
    IPAddress? BindAddress = null);

/// <summary>
/// A TCP client or server exposed to the app in the same terminal shape as a
/// serial/SSH transport. Servers keep the Python semantics: data from every
/// client is delivered to the terminal, and writes go to all clients unless a
/// specific client is selected.
/// </summary>
public interface ITcpSession : IDisposable
{
    PtyStreamConnection Pty { get; }

    bool IsConnected { get; }

    string LocalEndpoint { get; }

    IReadOnlyList<TcpClientInfo> Clients { get; }

    string? SelectedClient { get; }

    event EventHandler? ClientsChanged;

    void SelectClient(string? endpoint);

    void DisconnectClient(string? endpoint);
}
