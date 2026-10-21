using System.Diagnostics;
using COMTool.Sftp;
using Renci.SshNet;
using Renci.SshNet.Common;

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
    private readonly ConnectionInfo _connectionInfo;
    private SshSftpFileSystem? _sftp;
    private int _disposed;
    private int _transportFaulted;

    private SshConnection(
        SshClient client,
        ShellStream shell,
        PtyStreamConnection pty,
        TimeSpan connectTime,
        ConnectionInfo connectionInfo)
    {
        _client = client;
        _shell = shell;
        _connectionInfo = connectionInfo;
        Pty = pty;
        ConnectTime = connectTime;
        _shell.Closed += (_, _) => OnTransportFaulted();
        _shell.ErrorOccurred += (_, _) => OnTransportFaulted();
        _client.ErrorOccurred += (_, _) => OnTransportFaulted();
    }

    public PtyStreamConnection Pty { get; }

    public TimeSpan ConnectTime { get; }

    public bool IsConnected =>
        Volatile.Read(ref _transportFaulted) == 0
        && _client.IsConnected
        && !Pty.IsInputCompleted;

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

        var client = new SshClient(connectionInfo)
        {
            // Detect a silently dropped network path instead of waiting for
            // the next terminal keystroke to observe the broken socket.
            KeepAliveInterval = TimeSpan.FromSeconds(15),
        };
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
            var transportClosed = 0;
            var pty = new PtyStreamConnection(
                shell,
                shell,
                resize: (columns, rows) =>
                {
                    if (Volatile.Read(ref transportClosed) != 0)
                    {
                        return;
                    }

                    try
                    {
                        shell.ChangeWindowSize(
                            (uint)columns,
                            (uint)rows,
                            width: 0,
                            height: 0);
                    }
                    catch (Exception ex) when (
                        ex is ObjectDisposedException
                            or InvalidOperationException
                            or SshConnectionException
                            or IOException)
                    {
                        // The tab may close while Avalonia is still arranging
                        // the terminal. A best-effort resize must never escape
                        // into the UI layout pass.
                    }
                },
                close: () =>
                {
                    Interlocked.Exchange(ref transportClosed, 1);
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
            return new SshConnection(client, shell, pty, started.Elapsed, connectionInfo);
        }
        catch
        {
            started.Stop();
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the SFTP subsystem client. SSH.NET cannot share the channel
    /// of an open shell with the sftp subsystem, so this authenticates a
    /// second SSH connection with the same credentials. The client is
    /// created once and disposed together with the connection.
    /// </summary>
    public ISftpFileSystem CreateSftpFileSystem()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var existing = _sftp;
        if (existing is not null)
        {
            return existing;
        }

        var created = new SshSftpFileSystem(_connectionInfo);
        _sftp = created;
        return created;
    }

    /// <summary>
    /// The shell channel can end without the reader observing EOF, for example
    /// when the remote session process exits while the TCP connection remains
    /// open. The channel's Closed event is then the only liveness signal, and
    /// it must reach the tab even when terminal output is not being consumed.
    /// </summary>
    private void OnTransportFaulted()
    {
        if (Interlocked.Exchange(ref _transportFaulted, 1) != 0)
        {
            return;
        }

        _ = Task.Run(
            () =>
            {
                try
                {
                    Pty.Close(-1);
                }
                catch
                {
                    // The transport is already gone.
                }
            });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Close();
        _shell.Dispose();

        // The sftp client holds a second SSH connection of its own.
        var sftp = _sftp;
        _sftp = null;
        if (sftp is not null)
        {
            sftp.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _client.Dispose();
    }
}
