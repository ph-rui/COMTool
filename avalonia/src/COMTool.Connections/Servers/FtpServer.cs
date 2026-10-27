using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace COMTool.Connections.Servers;

/// <summary>
/// Minimal FTP server (RFC 959 subset) over a raw TcpListener:
/// USER/PASS (any credentials by default; when anonymous login is disabled
/// the configured user/password is required), SYST/FEAT/TYPE/NOOP, PASV data
/// channel, CWD/CDUP/PWD, LIST/NLST, RETR/STOR, SIZE, MKD/RMD/DELE, QUIT.
/// Every path stays under the configured root directory.
/// </summary>
public sealed class FtpServer : IServerHost
{
    private readonly string _root;
    private readonly int _requestedPort;
    private readonly bool _allowAnonymous;
    private readonly string _username;
    private readonly string _password;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public FtpServer(
        string rootDirectory,
        int port,
        bool allowAnonymous = true,
        string? username = "",
        string? password = "")
    {
        _root = Path.GetFullPath(rootDirectory);
        _requestedPort = port;
        _allowAnonymous = allowAnonymous;
        _username = username ?? string.Empty;
        _password = password ?? string.Empty;
    }

    public string Name => "FTP server";

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        var listener = new TcpListener(new IPEndPoint(IPAddress.Any, _requestedPort));
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token), CancellationToken.None);
        LogMessage?.Invoke($"FTP server started on port {Port}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _cts?.Cancel();
        _listener.Stop();
        _listener = null;

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            _acceptLoop = null;
        }

        _cts?.Dispose();
        _cts = null;
        LogMessage?.Invoke("FTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = Task.Run(
                () => HandleClientAsync(client, token),
                CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        LogMessage?.Invoke($"Client {remote} connected");
        var currentDir = string.Empty;
        string? providedUser = null;
        TcpListener? passive = null;

        try
        {
            using var stream = client.GetStream();
            await WriteAsync(stream, "220 COMTool FTP server ready\r\n").ConfigureAwait(false);
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var parts = line.Split(' ', 2);
                var cmd = parts[0].ToUpperInvariant();
                var arg = parts.Length > 1 ? parts[1] : string.Empty;

                switch (cmd)
                {
                    case "USER":
                        providedUser = arg;
                        await WriteAsync(stream, "331 Password required\r\n").ConfigureAwait(false);
                        break;
                    case "PASS":
                        {
                            var ok = _allowAnonymous;
                            if (!ok)
                            {
                                var expected = string.IsNullOrEmpty(_username)
                                    ? Environment.UserName
                                    : _username;
                                ok = providedUser is not null
                                    && string.Equals(providedUser, expected, StringComparison.OrdinalIgnoreCase)
                                    && arg == _password;
                            }
                            if (ok)
                            {
                                await WriteAsync(stream, "230 Login successful.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                providedUser = null;
                                await WriteAsync(stream, "530 Login incorrect.\r\n").ConfigureAwait(false);
                            }
                            break;
                        }
                    case "SYST":
                        await WriteAsync(stream, "215 UNIX Type: L8\r\n").ConfigureAwait(false);
                        break;
                    case "FEAT":
                        await WriteAsync(stream, "211-Features:\r\n PASV\r\n UTF8\r\n211 End\r\n").ConfigureAwait(false);
                        break;
                    case "TYPE":
                        await WriteAsync(stream, "200 Type set to I\r\n").ConfigureAwait(false);
                        break;
                    case "NOOP":
                        await WriteAsync(stream, "200 OK\r\n").ConfigureAwait(false);
                        break;
                    case "PWD":
                        await WriteAsync(stream, $"257 \"{EscapeFtp(currentDir)}\" is the current directory.\r\n").ConfigureAwait(false);
                        break;
                    case "CWD":
                        {
                            var next = ResolveSubPath(currentDir, arg);
                            if (next is null || !Directory.Exists(next))
                            {
                                await WriteAsync(stream, "550 Directory not available.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                currentDir = MakeRelative(next);
                                await WriteAsync(stream, "250 Directory changed.\r\n").ConfigureAwait(false);
                            }
                            break;
                        }
                    case "CDUP":
                        {
                            var parent = ResolveSubPath(currentDir, "..");
                            currentDir = parent is null ? string.Empty : MakeRelative(parent);
                            await WriteAsync(stream, "250 Directory changed.\r\n").ConfigureAwait(false);
                            break;
                        }
                    case "PASV":
                        {
                            passive = new TcpListener(IPAddress.Any, 0);
                            passive.Start();
                            var port = ((IPEndPoint)passive.Server.LocalEndPoint).Port;
                            var ip = client.Client.LocalEndPoint is IPEndPoint ip4 ? ip4.Address : IPAddress.Loopback;
                            await WriteAsync(
                                stream,
                                $"227 Entering Passive Mode ({string.Join(",", ip.GetAddressBytes().Select(b => b.ToString()))},{port >> 8},{port & 0xFF}).\r\n"
                            ).ConfigureAwait(false);
                            break;
                        }
                    case "LIST":
                    case "NLST":
                        {
                            var dir = ResolveSubPath(currentDir, cmd == "LIST" && !string.IsNullOrWhiteSpace(arg) ? arg : ".");
                            if (dir is null || !Directory.Exists(dir))
                            {
                                await WriteAsync(stream, "550 Directory not available.\r\n").ConfigureAwait(false);
                                break;
                            }

                            using var data = await OpenDataAsync(stream, passive, "150 Opening data connection for directory list.\r\n").ConfigureAwait(false);
                            if (data is null)
                            {
                                break;
                            }

                            var listing = new StringBuilder();
                            foreach (var (name, isDir, size) in SafeEnumerate(dir))
                            {
                                if (cmd == "NLST")
                                {
                                    listing.Append(name).Append('\n');
                                }
                                else
                                {
                                    listing.Append(isDir ? "drwxr-xr-x" : "-rw-r--r--")
                                        .Append("    1 ftp      ftp      ")
                                        .Append(PadLeft(isDir ? 4096L : size, 12))
                                        .Append(" Oct 26 12:00 ")
                                        .Append(name).Append('\n');
                                }
                            }

                            var bytes = Encoding.ASCII.GetBytes(listing.ToString());
                            await data.GetStream().WriteAsync(bytes, token).ConfigureAwait(false);
                            data.Close();
                            await WriteAsync(stream, "226 Directory send OK.\r\n").ConfigureAwait(false);
                            break;
                        }
                    case "RETR":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null || !File.Exists(path))
                            {
                                await WriteAsync(stream, "550 File not found.\r\n").ConfigureAwait(false);
                                break;
                            }

                            using var data = await OpenDataAsync(stream, passive, $"150 Opening data connection for {Path.GetFileName(path)}.\r\n").ConfigureAwait(false);
                            if (data is null)
                            {
                                break;
                            }

                            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            await file.CopyToAsync(data.GetStream(), 81920, token).ConfigureAwait(false);
                            data.Close();
                            await WriteAsync(stream, $"226 Transfer complete ({new FileInfo(path).Length} bytes).\r\n").ConfigureAwait(false);
                            LogMessage?.Invoke($"RETR {arg}");
                            break;
                        }
                    case "STOR":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null)
                            {
                                await WriteAsync(stream, "550 Path not allowed.\r\n").ConfigureAwait(false);
                                break;
                            }

                            using var data = await OpenDataAsync(stream, passive, $"150 Opening data connection for {Path.GetFileName(path)}.\r\n").ConfigureAwait(false);
                            if (data is null)
                            {
                                break;
                            }

                            var directory = Path.GetDirectoryName(path);
                            if (directory is not null && !Directory.Exists(directory))
                            {
                                data.Close();
                                await WriteAsync(stream, "550 Directory not available.\r\n").ConfigureAwait(false);
                                break;
                            }

                            long total = 0;
                            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                            var buffer = new byte[81920];
                            NetworkStream dataStream = data.GetStream();
                            int read;
                            while ((read = await dataStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                            {
                                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                                total += read;
                            }

                            data.Close();
                            await WriteAsync(stream, $"226 Transfer complete ({total} bytes).\r\n").ConfigureAwait(false);
                            LogMessage?.Invoke($"STOR {arg} ({total} bytes)");
                            break;
                        }
                    case "SIZE":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null || !File.Exists(path))
                            {
                                await WriteAsync(stream, "550 File not found.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                await WriteAsync(stream, $"213 {new FileInfo(path).Length}\r\n").ConfigureAwait(false);
                            }
                            break;
                        }
                    case "MKD":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null)
                            {
                                await WriteAsync(stream, "550 Path not allowed.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                Directory.CreateDirectory(path);
                                await WriteAsync(stream, $"257 \"{EscapeFtp(arg)}\" created.\r\n").ConfigureAwait(false);
                            }
                            break;
                        }
                    case "RMD":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null || !Directory.Exists(path))
                            {
                                await WriteAsync(stream, "550 Directory not available.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                Directory.Delete(path);
                                await WriteAsync(stream, "250 Directory deleted.\r\n").ConfigureAwait(false);
                            }
                            break;
                        }
                    case "DELE":
                        {
                            var path = ResolveSubPath(currentDir, arg);
                            if (path is null || !File.Exists(path))
                            {
                                await WriteAsync(stream, "550 File not found.\r\n").ConfigureAwait(false);
                            }
                            else
                            {
                                File.Delete(path);
                                await WriteAsync(stream, "250 Delete operation successful.\r\n").ConfigureAwait(false);
                                LogMessage?.Invoke($"DELE {arg}");
                            }
                            break;
                        }
                    case "QUIT":
                        await WriteAsync(stream, "221 Goodbye.\r\n").ConfigureAwait(false);
                        return;
                    default:
                        await WriteAsync(stream, "502 Command not implemented.\r\n").ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // server stopping
        }
        catch (Exception)
        {
            // client dropped mid-command
        }
        finally
        {
            passive?.Stop();
            client.Dispose();
            LogMessage?.Invoke($"Client {remote} disconnected");
        }
    }

    /// <summary>
    /// Sends the 1xx reply and accepts the passive data connection with a
    /// timeout. Returns null (after an error reply) when the client never
    /// connects.
    /// </summary>
    private static async Task<TcpClient?> OpenDataAsync(
        NetworkStream stream, TcpListener? passive, string readyReply)
    {
        await WriteAsync(stream, readyReply).ConfigureAwait(false);
        if (passive is null)
        {
            await WriteAsync(stream, "425 Use PASV first.\r\n").ConfigureAwait(false);
            return null;
        }

        var accept = passive.AcceptTcpClientAsync();
        if (await Task.WhenAny(accept, Task.Delay(15000)) != accept)
        {
            await WriteAsync(stream, "426 Data connection timeout.\r\n").ConfigureAwait(false);
            passive.Stop();
            return null;
        }

        return await accept.ConfigureAwait(false);
    }

    private static async Task WriteAsync(NetworkStream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <paramref name="sub"/> against the client's current
    /// directory and refuses anything that escapes the root.
    /// </summary>
    private string? ResolveSubPath(string currentDir, string sub)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(_root, currentDir, sub));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var prefix = _root.EndsWith(Path.DirectorySeparatorChar)
                ? _root
                : _root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, comparison) && !string.Equals(full, _root, comparison))
            {
                return null;
            }

            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private string MakeRelative(string full)
    {
        if (string.Equals(full, _root, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return full[_root.Length..].TrimStart(Path.DirectorySeparatorChar);
    }

    private static IEnumerable<(string Name, bool IsDir, long Size)> SafeEnumerate(string directory)
    {
        var entries = new List<(string, bool, long)>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory))
            {
                entries.Add((Path.GetFileName(dir), true, 4096));
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // keep the entry with size 0
                }

                entries.Add((Path.GetFileName(file), false, size));
            }
        }
        catch (IOException)
        {
            return entries;
        }

        return entries;
    }

    private static string PadLeft(long value, int width)
    {
        var text = value.ToString();
        return text.Length >= width ? text : new string(' ', width - text.Length) + text;
    }

    private static string EscapeFtp(string path) =>
        "/" + path.Replace(Path.DirectorySeparatorChar, '/');
}
