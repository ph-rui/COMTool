namespace COMTool.App.Services;

/// <summary>
/// Lightweight run log matching the Python app's run.log behavior. It is
/// intentionally independent of the UI so startup/config failures are
/// diagnosable even when no window can be created.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _initialized;

    public static string Path => _path ??= System.IO.Path.Combine(
        Core.Config.ConfigPaths.AvaloniaConfigDirectory(),
        "run.log");

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
            {
                var backup = Path + ".1";
                File.Delete(backup);
                File.Move(Path, backup);
            }

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Error("Unhandled exception", args.ExceptionObject as Exception);
            Write("INFO", $"COMTool Avalonia starting, log={Path}");
        }
        catch
        {
            // Logging must never prevent startup.
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception is null
            ? message
            : $"{message}: {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text);
        if (exception?.StackTrace is { Length: > 0 } stack)
        {
            Write("ERROR", stack);
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    Path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [{level}] - {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging is best-effort only.
        }
    }
}
