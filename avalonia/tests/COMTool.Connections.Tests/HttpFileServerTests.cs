using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections.Servers;

namespace COMTool.Connections.Tests;

public sealed class HttpFileServerTests : IDisposable
{
    private readonly string _parent = Path.Combine(
        Path.GetTempPath(),
        "comtool-http-tests",
        Guid.NewGuid().ToString("N"));

    private readonly string _root;

    public HttpFileServerTests()
    {
        _root = Path.Combine(_parent, "root");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_parent))
            {
                Directory.Delete(_parent, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact(Timeout = 20000)]
    public async Task ServesFileWithContentType()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "hello.txt"), "hello");
        await using var server = new HttpFileServer(_root, 0, allowDirectoryListing: true);
        await server.StartAsync();

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{server.Port}/hello.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.True(server.IsRunning);
        Assert.InRange(server.Port, 1, 65535);
    }

    [Fact(Timeout = 20000)]
    public async Task ServesDirectoryListingOnlyWhenEnabled()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "hello.txt"), "hello");

        await using (var listing = new HttpFileServer(_root, 0, allowDirectoryListing: true))
        {
            await listing.StartAsync();
            var (status, _, body) = await SendRawAsync(listing.Port, "/");
            Assert.Equal(200, status);
            Assert.Contains("hello.txt", body);
        }

        await using (var noListing = new HttpFileServer(_root, 0, allowDirectoryListing: false))
        {
            await noListing.StartAsync();
            var (status, _, _) = await SendRawAsync(noListing.Port, "/");
            Assert.Equal(403, status);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task RejectsTraversal()
    {
        await File.WriteAllTextAsync(Path.Combine(_parent, "secret.txt"), "top-secret");
        await using var server = new HttpFileServer(_root, 0, allowDirectoryListing: true);
        await server.StartAsync();

        var plain = await SendRawAsync(server.Port, "/../secret.txt");
        Assert.Equal(403, plain.Status);
        Assert.DoesNotContain("top-secret", plain.Body);

        var encoded = await SendRawAsync(server.Port, "/%2e%2e/secret.txt");
        Assert.Equal(403, encoded.Status);
        Assert.DoesNotContain("top-secret", encoded.Body);
    }

    [Fact(Timeout = 20000)]
    public async Task MissingFileReturns404()
    {
        await using var server = new HttpFileServer(_root, 0, allowDirectoryListing: true);
        await server.StartAsync();

        var (status, _, _) = await SendRawAsync(server.Port, "/nope.txt");
        Assert.Equal(404, status);
    }

    [Fact(Timeout = 20000)]
    public async Task HeadReturnsHeadersWithoutBody()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "hello.txt"), "hello");
        await using var server = new HttpFileServer(_root, 0, allowDirectoryListing: true);
        await server.StartAsync();

        var (status, headers, body) = await SendRawAsync(server.Port, "/hello.txt", "HEAD");
        Assert.Equal(200, status);
        Assert.Contains("Content-Length: 5", headers);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 20000)]
    public async Task StopClosesListener()
    {
        var server = new HttpFileServer(_root, 0, allowDirectoryListing: true);
        await server.StartAsync();
        var port = server.Port;
        await server.StopAsync();

        Assert.False(server.IsRunning);
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(
            () => client.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact(Timeout = 20000)]
    public async Task BusyPortThrows()
    {
        var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        try
        {
            var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            await using var server = new HttpFileServer(_root, port, allowDirectoryListing: true);
            await Assert.ThrowsAsync<SocketException>(() => server.StartAsync());
            Assert.False(server.IsRunning);
        }
        finally
        {
            occupied.Stop();
        }
    }

    private static async Task<(int Status, string Headers, string Body)> SendRawAsync(
        int port, string rawPath, string method = "GET")
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var request =
            $"{method} {rawPath} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = split >= 0 ? text[..split] : text;
        var body = split >= 0 ? text[(split + 4)..] : "";
        var statusLine = headers.Split("\r\n")[0];
        var status = int.Parse(statusLine.Split(' ')[1]);
        return (status, headers, body);
    }
}
