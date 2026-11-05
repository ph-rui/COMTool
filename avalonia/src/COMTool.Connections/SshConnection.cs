using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using COMTool.Connections.RemoteMonitoring;
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
public sealed class SshConnection : IDisposable, IExecRunner
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
    /// Runs a non-interactive command on a dedicated exec channel and returns
    /// its standard output. The interactive shell channel is not touched, so
    /// terminal I/O and monitoring polls can run concurrently. A hung remote
    /// command is abandoned after the timeout; the channel is always disposed.
    /// </summary>
    public async Task<string> RunCommandAsync(string command, int timeoutMs = 5000)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsConnected)
        {
            throw new IOException("The SSH connection is not open.");
        }

        // SshNet's SshCommand wraps a dedicated exec channel per call, so the
        // interactive shell channel is never touched. CommandTimeout abandons
        // a hung remote command; a failed command still returns whatever
        // stdout it produced - the monitor parser treats missing sections
        // as null instead of erroring.
        using (var cmd = _client.CreateCommand(command))
        {
            cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);
            await cmd.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            return cmd.Result;
        }
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

    // SSH.NET 的 ShellStream 不公开底层会话通道，而 RFC 4254 §6.5（信号）/§6.7（break）
    // 只能在承载 shell 的会话通道上发。取 ShellStream 私有字段 _channel
    // （运行时类型 Renci.SshNet.Channels.ChannelSession，internal 接口 IChannelSession 的
    // 实现）再调 SendSignalRequest/SendBreakRequest。SSH.NET 版本 pin 2026.0.0；若未来
    // 字段名变化，两个方法都静默返回 false，由调用方日志/提示，不影响其它功能。
    private static readonly object _sshProbeLock = new();
    private static FieldInfo? _sshChannelField;
    private static MethodInfo? _sshSignalMethod;
    private static MethodInfo? _sshBreakMethod;
    private static bool _sshProbeDone;

    // r52：Native AOT 下按名反射 GetField("_channel") 需要显式声明依赖，
    // 保证 trimmer 保留 ShellStream 的全部字段。
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.PublicFields,
        typeof(ShellStream))]
    private static void ProbeSshChannelOnce()
    {
        lock (_sshProbeLock)
        {
            if (_sshProbeDone)
            {
                return;
            }
            _sshProbeDone = true;
            try
            {
                _sshChannelField = typeof(ShellStream).GetField(
                    "_channel", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch
            {
                _sshChannelField = null;
            }
        }
    }

    private static object? GetChannelObject(ShellStream shell)
    {
        ProbeSshChannelOnce();
        if (_sshChannelField is null)
        {
            return null;
        }
        try
        {
            return _sshChannelField.GetValue(shell);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 发 SSH 会话信号请求（MobaXterm「特殊命令 → SIGxx」，RFC 4254 §6.5）。
    /// signalName 用协议名（"INT"/"TERM"/"KILL"/"HUP"…）。失败返回 false。
    /// </summary>
    // r52: AOT 下按名查找 ChannelSession 的公共方法，显式声明依赖保留成员
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        "Renci.SshNet.Channels.ChannelSession", "Renci.SshNet")]
    public bool TrySendSignal(string signalName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var channel = GetChannelObject(_shell);
        if (channel is null)
        {
            return false;
        }
        if (_sshSignalMethod is null)
        {
            // r52: 值经 object 流转，注解流无法证明；成员保留由 DynamicDependency 保证
#pragma warning disable IL2075
            _sshSignalMethod = channel.GetType().GetMethod(
                "SendSignalRequest", BindingFlags.Public | BindingFlags.Instance);
#pragma warning restore IL2075
        }
        if (_sshSignalMethod is null)
        {
            return false;
        }
        try
        {
            return (bool)(_sshSignalMethod.Invoke(channel, new object[] { signalName }) ?? false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发 SSH break 请求（MobaXterm「特殊命令 → Break」，RFC 4254 §6.7）。
    /// 失败返回 false。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        "Renci.SshNet.Channels.ChannelSession", "Renci.SshNet")]
    public bool TrySendBreak(uint durationMs = 250)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var channel = GetChannelObject(_shell);
        if (channel is null)
        {
            return false;
        }
        if (_sshBreakMethod is null)
        {
#pragma warning disable IL2075
            _sshBreakMethod = channel.GetType().GetMethod(
                "SendBreakRequest", BindingFlags.Public | BindingFlags.Instance);
#pragma warning restore IL2075
        }
        if (_sshBreakMethod is null)
        {
            return false;
        }
        try
        {
            return (bool)(_sshBreakMethod.Invoke(channel, new object[] { durationMs }) ?? false);
        }
        catch
        {
            return false;
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
