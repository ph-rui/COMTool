using System.Diagnostics;

namespace COMTool.Connections.Servers;

/// <summary>
/// Iperf throughput-test server: locates iperf3 (or iperf v2) on PATH and
/// runs it as a child process in server mode ("iperf3 -s -p PORT"), streaming
/// its output through LogMessage. When no iperf binary is installed the row
/// stays unstarted and the reason is reported in the server output.
/// </summary>
public sealed class IperfServer : IServerHost
{
    private readonly int _requestedPort;

    private Process? _process;
    private Task? _exitWatch;

    /// <summary>Test seam: force a specific binary path (null = search PATH).</summary>
    public string? BinaryOverride { get; set; }

    public IperfServer(int port)
    {
        _requestedPort = port;
    }

    public string Name => "Iperf server";

    public bool IsRunning => _process is { HasExited: false };

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var binary = BinaryOverride is not null ? BinaryOverride : FindIperf();
        if (binary is null || !File.Exists(binary))
        {
            LogMessage?.Invoke("未找到 iperf3 / iperf：请先安装 iperf3 并加入 PATH，再启动本服务器");
            return Task.CompletedTask;
        }

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = $"-s -p {_requestedPort}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 iperf 进程");
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Port = _requestedPort;
            _exitWatch = Task.Run(() => WatchExitAsync(process), CancellationToken.None);
            LogMessage?.Invoke($"iperf 已启动: {Path.GetFileName(binary)} -s -p {_requestedPort}");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"iperf 启动失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }

        process.Dispose();
        LogMessage?.Invoke("iperf 已停止");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            LogMessage?.Invoke(e.Data);
        }
    }

    private async Task WatchExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ReferenceEquals(_process, process))
        {
            // Unexpected self-exit (port conflict etc.): report it.
            LogMessage?.Invoke($"iperf 意外退出 (exit {process.ExitCode})");
            _process = null;
        }
    }

    private static string? FindIperf()
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { "iperf3.exe", "iperf.exe" }
            : new[] { "iperf3", "iperf" };

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // malformed PATH entry, skip
                }
            }
        }

        return null;
    }
}
