using System.Net;
using System.Net.Sockets;
using System.Text;

namespace COMTool.Connections.Servers;

/// <summary>
/// Minimal HTTP/1.1 file server over a raw <see cref="TcpListener"/>.
/// It deliberately avoids <c>HttpListener</c>, which needs an administrator
/// URL ACL reservation on Windows. Supports GET/HEAD, optional directory
/// listings, and guards against path traversal.
/// </summary>
public sealed class HttpFileServer : IServerHost
{
    private static readonly IReadOnlyDictionary<string, string> MimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".htm"] = "text/html; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".js"] = "application/javascript; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".txt"] = "text/plain; charset=utf-8",
            [".log"] = "text/plain; charset=utf-8",
            [".md"] = "text/plain; charset=utf-8",
            [".csv"] = "text/csv; charset=utf-8",
            [".xml"] = "application/xml; charset=utf-8",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".gz"] = "application/gzip",
            [".7z"] = "application/x-7z-compressed",
            [".bin"] = "application/octet-stream",
            [".hex"] = "application/octet-stream",
            [".elf"] = "application/octet-stream",
        };

    private readonly string _root;
    private readonly int _requestedPort;
    private readonly bool _allowDirectoryListing;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public HttpFileServer(string rootDirectory, int port, bool allowDirectoryListing)
    {
        _root = Path.GetFullPath(rootDirectory);
        _requestedPort = port;
        _allowDirectoryListing = allowDirectoryListing;
    }

    public string Name => "HTTP server";

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        LogMessage?.Invoke("Starting HTTP server, please wait...");
        var listener = new TcpListener(IPAddress.Any, _requestedPort);
        // Bind errors (port in use, access denied) intentionally propagate so
        // the dialog can log them and stay stopped.
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token), CancellationToken.None);
        LogMessage?.Invoke("HTTP server started");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        LogMessage?.Invoke("Stopping HTTP server, please wait...");
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
        }

        _acceptLoop = null;
        _cts?.Dispose();
        _cts = null;
        LogMessage?.Invoke("HTTP server stopped");
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
                if (token.IsCancellationRequested)
                {
                    break;
                }
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, token), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                var requestLine = await ReadRequestLineAsync(stream, timeout.Token)
                    .ConfigureAwait(false);
                if (requestLine is null)
                {
                    return;
                }

                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Bad request"), headOnly: false).ConfigureAwait(false);
                    return;
                }

                var method = parts[0].ToUpperInvariant();
                var target = parts[1];
                if (method is not ("GET" or "HEAD"))
                {
                    LogMessage?.Invoke($"{method} {target} 405");
                    await WriteResponseAsync(stream, 405, "Method Not Allowed",
                        "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Method not allowed"),
                        headOnly: method == "HEAD").ConfigureAwait(false);
                    return;
                }

                await ServeAsync(stream, method, target, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // client went away or the server stopped
            }
            catch (IOException)
            {
                // connection reset
            }
            catch (SocketException)
            {
                // connection reset
            }
        }
    }

    private async Task ServeAsync(NetworkStream stream, string method, string target,
        CancellationToken token)
    {
        var headOnly = method == "HEAD";
        var queryIndex = target.IndexOf('?');
        var pathPart = queryIndex >= 0 ? target[..queryIndex] : target;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(pathPart);
        }
        catch (UriFormatException)
        {
            await WriteStatusAsync(stream, 400, "Bad Request", headOnly).ConfigureAwait(false);
            return;
        }

        if (decoded.IndexOf('\0') >= 0)
        {
            await WriteStatusAsync(stream, 400, "Bad Request", headOnly).ConfigureAwait(false);
            return;
        }

        var relative = decoded.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(_root, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            await WriteStatusAsync(stream, 400, "Bad Request", headOnly).ConfigureAwait(false);
            return;
        }

        if (!IsInsideRoot(fullPath))
        {
            LogMessage?.Invoke($"{method} {target} 403");
            await WriteStatusAsync(stream, 403, "Forbidden", headOnly).ConfigureAwait(false);
            return;
        }

        if (Directory.Exists(fullPath))
        {
            if (!decoded.EndsWith('/'))
            {
                var location = target + "/";
                LogMessage?.Invoke($"{method} {target} 301");
                await WriteResponseAsync(stream, 301, "Moved Permanently",
                    "text/plain; charset=utf-8", Array.Empty<byte>(), headOnly,
                    extraHeaders: $"Location: {location}\r\n").ConfigureAwait(false);
                return;
            }

            if (!_allowDirectoryListing)
            {
                LogMessage?.Invoke($"{method} {target} 403");
                await WriteStatusAsync(stream, 403, "Forbidden", headOnly).ConfigureAwait(false);
                return;
            }

            var html = BuildDirectoryListing(decoded, fullPath);
            LogMessage?.Invoke($"{method} {target} 200");
            await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8",
                Encoding.UTF8.GetBytes(html), headOnly).ConfigureAwait(false);
            return;
        }

        if (!File.Exists(fullPath))
        {
            LogMessage?.Invoke($"{method} {target} 404");
            await WriteStatusAsync(stream, 404, "Not Found", headOnly).ConfigureAwait(false);
            return;
        }

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(fullPath, token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            LogMessage?.Invoke($"{method} {target} 500");
            await WriteStatusAsync(stream, 500, "Internal Server Error", headOnly)
                .ConfigureAwait(false);
            return;
        }

        var contentType = MimeTypes.TryGetValue(Path.GetExtension(fullPath), out var mime)
            ? mime
            : "application/octet-stream";
        LogMessage?.Invoke($"{method} {target} 200");
        await WriteResponseAsync(stream, 200, "OK", contentType, content, headOnly)
            .ConfigureAwait(false);
    }

    private bool IsInsideRoot(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullPath, _root, comparison))
        {
            return true;
        }

        var prefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream,
        CancellationToken token)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), token)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return text[..end];
            }
        }

        return total == 0 ? null : Encoding.ASCII.GetString(buffer, 0, total).Trim();
    }

    private static async Task WriteStatusAsync(NetworkStream stream, int code, string reason,
        bool headOnly)
    {
        var body = Encoding.UTF8.GetBytes($"{code} {reason}");
        await WriteResponseAsync(stream, code, reason, "text/plain; charset=utf-8", body, headOnly)
            .ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int code, string reason,
        string contentType, byte[] body, bool headOnly, string extraHeaders = "")
    {
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Connection: close\r\n")
            .Append(extraHeaders)
            .Append("\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers)).ConfigureAwait(false);
        if (!headOnly && body.Length > 0)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string BuildDirectoryListing(string urlPath, string directory)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Index of ")
          .Append(WebUtility.HtmlEncode(urlPath))
          .Append("</title></head><body><h1>Index of ")
          .Append(WebUtility.HtmlEncode(urlPath))
          .Append("</h1><hr><ul>");

        if (urlPath != "/")
        {
            sb.Append("<li><a href=\"../\">../</a></li>");
        }

        IEnumerable<string> directories = Directory.EnumerateDirectories(directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files = Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            var name = WebUtility.HtmlEncode(dir);
            sb.Append("<li><a href=\"").Append(Uri.EscapeDataString(dir!)).Append("/\">")
              .Append(name).Append("/</a></li>");
        }

        foreach (var file in files)
        {
            var name = WebUtility.HtmlEncode(file);
            sb.Append("<li><a href=\"").Append(Uri.EscapeDataString(file!)).Append("\">")
              .Append(name).Append("</a></li>");
        }

        sb.Append("</ul><hr></body></html>");
        return sb.ToString();
    }
}
