using System.Diagnostics;
using System.Text;
using COMTool.Connections;
using COMTool.Core.Config;
using COMTool.Terminal;

namespace COMTool.Phase0Smoke;

internal static class Program
{
    private static int _failures;

    private static async Task<int> Main(string[] args)
    {
        var parsed = Parse(args);
        Console.WriteLine($"COMTool Phase 0 smoke on {Environment.OSVersion} ({Environment.Version})");

        if (parsed.ContainsKey("dpapi"))
        {
            Run("DPAPI", CheckDpapi);
        }

        if (parsed.ContainsKey("config"))
        {
            Run("Config import", CheckConfigImport);
        }

        if (parsed.ContainsKey("terminal"))
        {
            Run("Terminal benchmark", CheckTerminal);
        }

        if (parsed.ContainsKey("ports"))
        {
            Run("Serial ports", CheckPorts);
        }

        if (parsed.TryGetValue("serial", out var serial))
        {
            Run("Serial open", () => CheckSerial(serial));
        }

        if (parsed.TryGetValue("ssh", out var ssh))
        {
            await RunAsync("SSH shell", () => CheckSsh(ssh));
        }

        if (parsed.TryGetValue("sshpass", out var sshPassword))
        {
            await RunAsync("SSH password", () => CheckSshPassword(sshPassword));
        }

        Console.WriteLine(_failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_failures})");
        return _failures == 0 ? 0 : 1;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = arg[2..];
            var value = "";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            values[name] = value;
        }

        return values;
    }

    private static void Run(string name, Action check)
    {
        try
        {
            check();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task RunAsync(string name, Func<Task> check)
    {
        try
        {
            await check();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CheckDpapi()
    {
        if (!SecretProtector.IsSupported)
        {
            Console.WriteLine("skip DPAPI: not Windows");
            return;
        }

        const string secret = "COMTool DPAPI smoke 中文";
        var token = SecretProtector.Protect(secret);
        if (token.Length == 0)
        {
            throw new InvalidOperationException("Protect returned an empty token");
        }

        var plain = SecretProtector.Unprotect(token);
        if (plain != secret)
        {
            throw new InvalidOperationException("Unprotect did not round-trip");
        }

        Console.WriteLine($"DPAPI token length={token.Length}, round-trip ok");
    }

    private static void CheckConfigImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "comtool-phase0-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "python.json");
            var target = Path.Combine(root, "avalonia.json");
            File.WriteAllText(source, """{"version":3,"skin":"dark","passwd_enc":"token"}""");
            var result = PythonConfigImporter.ImportIfMissing(source, target);
            if (!result.Imported || !File.Exists(target))
            {
                throw new InvalidOperationException(result.Status.ToString());
            }

            if (File.ReadAllText(source) != File.ReadAllText(target))
            {
                throw new InvalidOperationException("imported content differs");
            }

            var second = PythonConfigImporter.ImportIfMissing(source, target);
            if (second.Status != ConfigImportStatus.TargetExists)
            {
                throw new InvalidOperationException($"second import was {second.Status}");
            }

            Console.WriteLine("one-way import and no-overwrite ok");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckTerminal()
    {
        using var terminal = new TerminalEmulator(columns: 120, rows: 40);
        var chunk = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("COMTool terminal 0123456789 abcdefghijklmnopqrstuvwxyz\r\n", 1000)));
        var oneMb = Measure(terminal, chunk, 1_048_576);
        var tenMb = Measure(terminal, chunk, 10_485_760);
        Console.WriteLine($"1MB={oneMb.TotalMilliseconds:F1}ms, 10MB={tenMb.TotalMilliseconds:F1}ms");
        if (oneMb > TimeSpan.FromSeconds(15) || tenMb > TimeSpan.FromSeconds(20))
        {
            throw new InvalidOperationException("terminal throughput exceeded the Phase 0 budget");
        }
    }

    private static TimeSpan Measure(TerminalEmulator terminal, byte[] chunk, long target)
    {
        var watch = Stopwatch.StartNew();
        long total = 0;
        while (total < target)
        {
            terminal.Write(chunk);
            total += chunk.Length;
        }

        watch.Stop();
        return watch.Elapsed;
    }

    private static void CheckSerial(string port)
    {
        using var connection = SerialConnection.Open(new SerialConnectionConfig
        {
            Port = port,
            Baudrate = 115200,
            Bytesize = 8,
            Parity = "None",
            Stopbits = "1",
            Flowcontrol = "None",
        });
        Console.WriteLine($"opened {port}, open={connection.IsOpen}");
    }

    private static void CheckPorts()
    {
        var scan = SerialPortCatalog.Scan();
        Console.WriteLine(
            $"success={scan.Success}, ports={string.Join(", ", scan.Ports.Select(p => p.DisplayText))}");
        if (!scan.Success)
        {
            throw new InvalidOperationException(scan.Error);
        }
    }

    private static async Task CheckSsh(string specification)
    {
        var parts = specification.Split(':', 4);
        if (parts.Length < 3)
        {
            throw new ArgumentException("expected host:port:user:keyfile");
        }

        var host = parts[0];
        var port = int.Parse(parts[1]);
        var user = parts[2];
        var keyFile = parts.Length > 3 ? parts[3] : "";
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var connection = SshConnection.Connect(new SshConnectionOptions(
                host,
                port,
                user,
                KeyFile: keyFile,
                Columns: 100,
                Rows: 40,
                Timeout: TimeSpan.FromSeconds(20)));
            Console.WriteLine($"attempt {attempt}:");
            await VerifyShell(connection);
        }
    }

    private static async Task CheckSshPassword(string specification)
    {
        var parts = specification.Split(':', 3);
        if (parts.Length < 3)
        {
            throw new ArgumentException("expected host:port:user");
        }

        var password = Environment.GetEnvironmentVariable("COMTOOL_SSH_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("COMTOOL_SSH_PASSWORD is not set");
        }

        using var connection = SshConnection.Connect(new SshConnectionOptions(
            parts[0],
            int.Parse(parts[1]),
            parts[2],
            Password: password,
            Columns: 100,
            Rows: 40,
            Timeout: TimeSpan.FromSeconds(20)));
        await VerifyShell(connection, verifyResize: false);
    }

    private static async Task VerifyShell(SshConnection connection, bool verifyResize = true)
    {
        Console.WriteLine($"connected in {connection.ConnectTime.TotalMilliseconds:F0}ms, connected={connection.IsConnected}");

        var marker = $"COMToolSSHOK-{Guid.NewGuid():N}";
        var output = await ReadCommand(
            connection,
            $"printf '{marker}\\n'\n",
            marker,
            TimeSpan.FromSeconds(10));
        if (!output.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("shell marker was not returned");
        }

        connection.Pty.Resize(100, 40);
        if (!verifyResize)
        {
            Console.WriteLine("shell output ok; resize check skipped for this target");
            return;
        }

        var size = await ReadCommand(connection, "stty size\n", "40 100", TimeSpan.FromSeconds(10));
        if (!size.Contains("40 100", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PTY did not resize to 40x100");
        }

        Console.WriteLine("shell output and PTY resize ok");
    }

    private static async Task<string> ReadCommand(
        SshConnection connection,
        string command,
        string marker,
        TimeSpan timeout)
    {
        var writer = connection.Pty.WriterStream;
        writer.Write(Encoding.UTF8.GetBytes(command));
        writer.Flush();

        var reader = connection.Pty.ReaderStream;
        var buffer = new byte[4096];
        var text = new StringBuilder();
        var watch = Stopwatch.StartNew();
        var readTask = Task.Run(() => reader.Read(buffer, 0, buffer.Length));
        while (watch.Elapsed < timeout)
        {
            var remaining = timeout - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var completed = await Task.WhenAny(readTask, Task.Delay(remaining));
            if (completed != readTask)
            {
                break;
            }

            var count = await readTask;
            if (count <= 0)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, count));
            if (text.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return text.ToString();
            }

            readTask = Task.Run(() => reader.Read(buffer, 0, buffer.Length));
        }

        return text.ToString();
    }
}
