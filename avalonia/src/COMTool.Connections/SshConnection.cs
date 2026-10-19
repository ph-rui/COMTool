using System.Diagnostics;
using Renci.SshNet;

namespace COMTool.Connections;

public sealed record SshConnectionOptions(
    string Host,
    int Port,
    string User,
    string? Password = null,
    string? KeyFile = null,
    string? KeyPassphrase = null,
    int Columns = 80,
    int Rows = 24,
    TimeSpan? Timeout = null);

/// <summary>
/// SSH.NET shell connection exposed as an IPtyConnection. Authentication
/// order is explicit key, then explicit password; callers are responsible for
/// looking up cached or DPAPI-protected passwords before constructing this.
/// </summary>
public sealed class SshConnection : IDisposable
{
    private readonly SshClient _client;
    private readonly ShellStream _shell;
    private int _disposed;

    private SshConnection(
        SshClient client,
        ShellStream shell,
        PtyStreamConnection pty,
        TimeSpan connectTime)
    {
        _client = client;
        _shell = shell;
        Pty = pty;
        ConnectTime = connectTime;
    }

    public PtyStreamConnection Pty { get; }

    public TimeSpan ConnectTime { get; }

    public bool IsConnected => _client.IsConnected;

    public static SshConnection Connect(SshConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("SSH host is empty", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.User))
        {
            throw new ArgumentException("SSH user is empty", nameof(options));
        }

        var timeout = options.Timeout ?? TimeSpan.FromSeconds(10);
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(options.KeyFile))
        {
            var key = new PrivateKeyFile(
                options.KeyFile,
                string.IsNullOrEmpty(options.KeyPassphrase)
                    ? null
                    : options.KeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(options.User, key));
        }

        if (!string.IsNullOrEmpty(options.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(options.User, options.Password));
        }

        if (methods.Count == 0)
        {
            throw new ArgumentException(
                "SSH connection requires a key file or password",
                nameof(options));
        }

        var connectionInfo = new ConnectionInfo(
            options.Host,
            options.Port <= 0 ? 22 : options.Port,
            options.User,
            methods.ToArray())
        {
            Timeout = timeout,
        };

        var client = new SshClient(connectionInfo);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            client.Connect();
            started.Stop();
            var shell = client.CreateShellStream(
                "xterm-256color",
                (uint)Math.Max(1, options.Columns),
                (uint)Math.Max(1, options.Rows),
                width: 0,
                height: 0,
                bufferSize: 4096);
            var pty = new PtyStreamConnection(
                shell,
                shell,
                resize: (columns, rows) => shell.ChangeWindowSize(
                    (uint)columns,
                    (uint)rows,
                    width: 0,
                    height: 0),
                close: () =>
                {
                    try
                    {
                        shell.Dispose();
                    }
                    finally
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }

                        client.Dispose();
                    }
                },
                ownsStreams: false);
            return new SshConnection(client, shell, pty, started.Elapsed);
        }
        catch
        {
            started.Stop();
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Close();
        _shell.Dispose();
        _client.Dispose();
    }
}
