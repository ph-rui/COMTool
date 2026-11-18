using System.Text.Json;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Services;

/// <summary>
/// Owns the Avalonia Beta config, its session stores and the latest serial
/// scan. A failed serial scan is never used to prune remembered sessions.
/// </summary>
public sealed class AppConfigService
{
    private readonly SerialSessionStore _serialSessions = new();
    private readonly SshSessionStore _sshSessions = new();
    private readonly TelnetSessionStore _telnetSessions = new();
    private readonly TcpSessionStore _tcpSessions = new();

    public static WindowConfig? TryReadWindowConfig()
    {
        try
        {
            var path = ConfigPaths.AvaloniaConfigFile();
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ComtoolConfig>(File.ReadAllText(path), ConfigJsonContext.Default.ComtoolConfig)?.Window;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to read window size", ex);
            return null;
        }
    }

    private AppConfigService(ComtoolConfig config, string configPath)
    {
        Config = config;
        ConfigPath = configPath;
        _serialSessions.Load(config);
        _sshSessions.Load(config);
        _telnetSessions.Load(config);
        _tcpSessions.Load(config);
    }

    public ComtoolConfig Config { get; }

    public string ConfigPath { get; }

    public SerialSessionStore SerialSessions => _serialSessions;

    public SshSessionStore SshSessions => _sshSessions;

    public TelnetSessionStore TelnetSessions => _telnetSessions;

    public TcpSessionStore TcpSessions => _tcpSessions;

    public SshPasswordCache PasswordCache { get; } = new();

    public ConfigImportResult? ImportResult { get; private set; }

    public IReadOnlyList<SerialPortInfo> Ports { get; private set; } = Array.Empty<SerialPortInfo>();

    public IReadOnlyDictionary<string, SerialPortInfo> PortMap { get; private set; } =
        new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True after at least one successful scan. A failed scan must not be
    /// interpreted as "all devices disappeared".
    /// </summary>
    public bool PortScanSucceeded { get; private set; }

    public static AppConfigService Load(Func<SerialPortScanResult>? scanPorts = null)
    {
        var import = PythonConfigImporter.ImportIfMissing();
        var path = ConfigPaths.AvaloniaConfigFile();
        ComtoolConfig config;
        try
        {
            config = ComtoolConfig.Load(path);
        }
        catch (Exception ex)
        {
            // r67.7：配置读不出来（断电/强杀后文件被清零，实机 2026-11-18 遇到
            // 8.7KB 全 0x00）时不能让应用「无配置运行」——_config 为 null 后既不读
            // 也不写，用户看到的永远是空会话列表、之后改的设置也不落盘。
            // 把坏文件改名留证，按默认配置继续并立刻写一份可用的配置。
            AppLog.Error("Config unreadable; quarantining and resetting to defaults", ex);
            var quarantine = $"{path}.corrupt.{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            try
            {
                if (File.Exists(path))
                {
                    File.Move(path, quarantine, overwrite: true);
                }
            }
            catch (Exception moveEx)
            {
                AppLog.Error($"Failed to quarantine corrupt config to {quarantine}", moveEx);
            }

            config = new ComtoolConfig();
            try
            {
                config.Save(path);
            }
            catch (Exception saveEx)
            {
                AppLog.Error("Failed to write default config after corruption", saveEx);
            }
        }

        var service = new AppConfigService(config, path)
        {
            ImportResult = import,
        };
        service.RefreshPorts(scanPorts?.Invoke() ?? SerialPortCatalog.Scan());
        return service;
    }

    public SerialPortScanResult RefreshPorts() => RefreshPorts(SerialPortCatalog.Scan());

    public SerialPortScanResult RefreshPorts(SerialPortScanResult scan)
    {
        if (!scan.Success)
        {
            // Keep the previous port map and all remembered serial sessions.
            return scan;
        }

        Ports = scan.Ports;
        PortMap = scan.Ports.ToDictionary(
            port => port.Device,
            port => port,
            StringComparer.OrdinalIgnoreCase);
        PortScanSucceeded = true;

        // Remembered serial sessions intentionally survive unplugging. The
        // sidebar hides unavailable ports and shows them again when the same
        // COM device returns; deleting the session here would make hotplug
        // recovery impossible.
        return scan;
    }

    public void Save()
    {
        _serialSessions.Save(Config);
        _sshSessions.Save(Config);
        _telnetSessions.Save(Config);
        _tcpSessions.Save(Config);
        Config.Save(ConfigPath);
    }

    /// <summary>
    /// Persists only the app config (session stores untouched). Used by
    /// lightweight preferences such as the remembered network scan range.
    /// </summary>
    public void SaveConfig() => Config.Save(ConfigPath);

    public void RememberSshPassword(
        SshSession session,
        string password,
        bool remember)
    {
        if (!remember || password.Length == 0)
        {
            return;
        }

        var token = SecretProtector.Protect(password);
        if (token.Length == 0)
        {
            return;
        }

        _sshSessions.Upsert(
            session.Host,
            session.Port,
            session.User,
            alias: session.Alias,
            sshKey: session.SshKey,
            encryptedPassword: token);
        PasswordCache.Set(session.Key, password);
        Save();
    }

    public void RememberTelnetPassword(
        TelnetSession session,
        string password,
        bool remember)
    {
        if (!remember || password.Length == 0)
        {
            return;
        }

        var token = SecretProtector.Protect(password);
        if (token.Length == 0)
        {
            return;
        }

        _telnetSessions.Upsert(
            session.Host,
            session.Port,
            session.User,
            alias: session.Alias,
            encryptedPassword: token);
        PasswordCache.Set(session.Key, password);
        Save();
    }
}
