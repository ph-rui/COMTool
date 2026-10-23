namespace COMTool.Connections.Servers;

/// <summary>
/// A local server exposed in the MobaXterm-style "Servers management" dialog.
/// Implementations own a listener and report human readable lifecycle and
/// request messages through <see cref="LogMessage"/>.
/// </summary>
public interface IServerHost : IAsyncDisposable
{
    /// <summary>Display name used in log context, e.g. "HTTP server".</summary>
    string Name { get; }

    bool IsRunning { get; }

    /// <summary>Bound TCP/UDP port (after <see cref="StartAsync"/>).</summary>
    int Port { get; }

    /// <summary>
    /// Raw log line (no timestamp); the UI prefixes "[HH:mm:ss] ". May be
    /// raised from a background thread.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>Binds the listener and starts serving. Throws on bind failure.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops serving and closes the listener. Safe to call twice.</summary>
    Task StopAsync();
}
