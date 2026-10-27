namespace COMTool.Connections.Servers;

/// <summary>
/// Placeholder host for server rows that are present in the UI (1:1 with
/// MobaXterm) but not implemented as real services yet (NFS, VNC). Starting
/// it reports the reason in the server output and leaves the row unstarted.
/// </summary>
public sealed class StubServer : IServerHost
{
    private readonly string _name;
    private readonly string _message;

    public StubServer(string name, string message)
    {
        _name = name;
        _message = message;
    }

    public string Name => _name;

    public bool IsRunning => false;

    public int Port => 0;

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        LogMessage?.Invoke(_message);
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
