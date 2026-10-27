using System.Diagnostics;

namespace COMTool.Connections.Servers;

/// <summary>
/// Pure 5-field cron expression matcher (minute hour day-of-month month
/// day-of-week). Supports "*", "N", "A-B", "*/N", "A-B/N" and comma lists.
/// Day-of-week uses 0-6 with 0 = Sunday. No I/O and no timers, so the
/// matcher is fully unit-testable.
/// </summary>
public sealed class CronPattern
{
    private readonly bool[][] _masks;

    private CronPattern(bool[][] masks)
    {
        _masks = masks;
    }

    public static bool TryParse(string expression, out CronPattern pattern, out string error)
    {
        pattern = null!;
        error = string.Empty;

        var parts = expression?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();
        if (parts.Length != 5)
        {
            error = "cron 表达式必须是 5 段（分 时 日 月 周）";
            return false;
        }

        var specs = new (int Min, int Max)[]
        {
            (0, 59),   // minute
            (0, 23),   // hour
            (1, 31),   // day of month
            (1, 12),   // month
            (0, 6),    // day of week, 0 = Sunday
        };

        var masks = new bool[5][];
        for (var i = 0; i < 5; i++)
        {
            if (!TryParseField(parts[i], specs[i].Min, specs[i].Max, out masks[i], out error))
            {
                return false;
            }
        }

        pattern = new CronPattern(masks);
        return true;
    }

    public bool Matches(DateTime time)
    {
        // Masks are indexed relative to each field's minimum (minute/hour/
        // day-of-week start at 0, day-of-month and month start at 1).
        return _masks[0][time.Minute]
            && _masks[1][time.Hour]
            && _masks[2][time.Day - 1]
            && _masks[3][time.Month - 1]
            && _masks[4][(int)time.DayOfWeek]; // .NET Sunday = 0, cron convention
    }

    private static bool TryParseField(
        string text, int min, int max, out bool[] mask, out string error)
    {
        mask = new bool[max - min + 1];
        error = string.Empty;

        foreach (var part in text.Split(','))
        {
            if (part.Length == 0)
            {
                error = "cron 字段存在空项";
                return false;
            }

            var step = 1;
            var body = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                body = part[..slash];
                if (!int.TryParse(part[(slash + 1)..], out step) || step < 1)
                {
                    error = $"cron 步长非法: {part}";
                    return false;
                }
            }

            int lo, hi;
            if (body == "*")
            {
                lo = min;
                hi = max;
            }
            else if (body.Contains('-'))
            {
                var bounds = body.Split('-');
                if (bounds.Length != 2
                    || !int.TryParse(bounds[0], out lo)
                    || !int.TryParse(bounds[1], out hi))
                {
                    error = $"cron 范围非法: {part}";
                    return false;
                }
            }
            else if (int.TryParse(body, out var single))
            {
                lo = single;
                hi = slash < 0 ? single : max; // "N/step" starts at N, runs to max
            }
            else
            {
                error = $"cron 项非法: {part}";
                return false;
            }

            if (lo < min || hi > max || lo > hi)
            {
                error = $"cron 值超出 {min}-{max}: {part}";
                return false;
            }

            for (var v = lo; v <= hi; v += step)
            {
                mask[v - min] = true;
            }
        }

        return true;
    }
}

/// <summary>
/// Runs a local command on every cron-matched minute (5-field expression).
/// Output of each run is streamed through LogMessage. At most one command
/// runs at a time: a still-running job skips its tick.
/// </summary>
public sealed class CronServer : IServerHost
{
    private readonly string _expression;
    private readonly string _command;
    private readonly CronPattern _pattern;
    private readonly string _parseError;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Process? _current;
    private DateTime _lastRunMinute = DateTime.MinValue;
    private bool _busy;

    public CronServer(string expression, string command)
    {
        _expression = expression;
        _command = command;
        _parseError = string.Empty;
        _pattern = CronPattern.TryParse(expression, out var parsed, out var error) ? parsed : null!;
        if (parsed is null)
        {
            _parseError = error;
        }
    }

    public string Name => "Cron server";

    public bool IsRunning { get; private set; }

    public int Port => 0;

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        if (_pattern is null)
        {
            LogMessage?.Invoke($"Cron 表达式无效：{_parseError}");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_command))
        {
            LogMessage?.Invoke("执行命令为空，Cron 服务器已就绪但不会执行任务");
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => TickLoopAsync(_cts.Token), CancellationToken.None);
        IsRunning = true;
        LogMessage?.Invoke($"Cron server started ({_expression})");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning && _cts is null)
        {
            return;
        }

        _cts?.Cancel();
        var loop = _loop;
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        IsRunning = false;

        KillCurrent();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        LogMessage?.Invoke("Cron server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task TickLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            if (now.Second == 0
                && _lastRunMinute != now
                && _pattern.Matches(now)
                && !_busy)
            {
                _lastRunMinute = now;
                if (!string.IsNullOrWhiteSpace(_command))
                {
                    _ = Task.Run(() => RunCommandAsync(token), CancellationToken.None);
                }
            }

            try
            {
                await Task.Delay(250, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCommandAsync(CancellationToken token)
    {
        _busy = true;
        LogMessage?.Invoke($"[cron] 执行: {_command}");
        try
        {
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c {_command}";
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = $"-c {_command}";
            }

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动命令进程");
            _current = process;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            var outText = (await stdout.ConfigureAwait(false)).Trim();
            var errText = (await stderr.ConfigureAwait(false)).Trim();
            if (outText.Length > 0)
            {
                LogMessage?.Invoke($"[cron] {outText}");
            }
            if (errText.Length > 0)
            {
                LogMessage?.Invoke($"[cron][err] {errText}");
            }
            LogMessage?.Invoke($"[cron] 结束 (exit {process.ExitCode})");

            _current = null;
        }
        catch (OperationCanceledException)
        {
            KillCurrent();
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[cron] 执行失败: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void KillCurrent()
    {
        var process = _current;
        _current = null;
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
            process?.Dispose();
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
    }
}
