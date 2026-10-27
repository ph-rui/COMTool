using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Universal.Common.Ssh;
using Universal.Common.Ssh.Server;

namespace COMTool.Connections.Servers;

/// <summary>
/// SSH/SFTP server host built on Universal.Common.Ssh (CC0). Mirrors
/// MobaXterm's built-in server: only the current Windows login name is
/// accepted as the user name (the password cannot be validated locally, so
/// any password is accepted for that user). Serves SFTP v3 from the
/// configured root directory and refuses shell/exec channels. A fresh
/// Ed25519 host key is generated on every start; its SHA-256 fingerprint is
/// logged so clients can verify it (connect with -o StrictHostKeyChecking=no
/// to skip the prompt).
/// </summary>
public sealed class SshServerHost : IServerHost
{
    private readonly string _root;
    private readonly int _requestedPort;

    private SshServer? _server;
    private SshEd25519Key? _hostKey;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public SshServerHost(string rootDirectory, int port)
    {
        _root = Path.GetFullPath(rootDirectory);
        _requestedPort = port;
    }

    public string Name => "SSH/SFTP server";

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_server is not null)
        {
            return Task.CompletedTask;
        }

        // Fail fast on a taken port (ListenAsync would report it late).
        var endpoint = new IPEndPoint(IPAddress.Any, _requestedPort);
        using (var probe = new TcpListener(endpoint))
        {
            probe.Start();
            probe.Stop();
        }

        _hostKey = SshEd25519Key.Generate();
        // MobaXterm's built-in server only accepts the current Windows login
        // (with the associated password). We cannot validate the Windows
        // password locally, so the user name is checked and any password is
        // accepted for that user.
        var expectedUser = Environment.UserName;
        var authenticator = new SshDelegateAuthenticator(
            passwordAuthenticator: (userName, supplied) =>
                string.Equals(userName, expectedUser, StringComparison.OrdinalIgnoreCase));
        var server = new SshServer(new SshServerOptions(), _hostKey, authenticator, HandleSessionAsync);

        _cts = new CancellationTokenSource();
        _server = server;
        Port = _requestedPort;
        _listenTask = server.ListenAsync(endpoint, _cts.Token);
        IsRunning = true;

        LogMessage?.Invoke($"SSH/SFTP server started on port {Port}");
        LogMessage?.Invoke($"host key SHA256:{Fingerprint(_hostKey)}");
        LogMessage?.Invoke($"connect e.g.: sftp -o StrictHostKeyChecking=no {expectedUser}@<ip> (any password)");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_server is null)
        {
            return;
        }

        IsRunning = false;
        _cts?.Cancel();
        var task = _listenTask;
        var server = _server;
        _server = null;
        _listenTask = null;

        server.Dispose();
        _hostKey?.Dispose();
        _hostKey = null;

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            catch (Exception)
            {
                // bind race or client error - already stopped
            }
        }

        _cts?.Dispose();
        _cts = null;
        LogMessage?.Invoke("SSH/SFTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static string Fingerprint(SshEd25519Key key)
    {
        var hash = SHA256.HashData(key.PublicKeyBlob.Span);
        return Convert.ToBase64String(hash);
    }

    private async Task HandleSessionAsync(SshServerSessionContext context, CancellationToken ct)
    {
        LogMessage?.Invoke($"client authenticated as '{context.UserName}'");
        // SftpServer.RunAsync reads the FIRST channel request itself and
        // expects it to be "subsystem sftp": it replies to the request and
        // then drives the whole SFTP v3 protocol on the channel. Any other
        // first request (pty-req / shell / exec) is declined by the library
        // and the channel is closed, which is exactly the SFTP-only posture.
        // The handler must NOT call ReadRequestAsync first - consuming the
        // request there leaves RunAsync waiting for a request that never
        // arrives and it tears the channel down before the SFTP init reply.
        try
        {
            var sftp = new SftpServer(_root);
            await sftp.RunAsync(context.Channel, ct).ConfigureAwait(false);
            LogMessage?.Invoke("sftp session ended");
        }
        catch (OperationCanceledException)
        {
            // server stopping
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"session ended: {ex.Message}");
        }
    }
}
