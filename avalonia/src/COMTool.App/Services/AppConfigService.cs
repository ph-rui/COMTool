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

            return JsonSerializer.Deserialize<ComtoolConfig>(File.ReadAllText(path))?.Window;
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
        _tcpSessions.Load(config);
    }

    public ComtoolConfig Config { get; }

    public string ConfigPath { get; }

    public SerialSessionStore SerialSessions => _serialSessions;

    public SshSessionStore SshSessions => _sshSessions;

    public TcpSessionStore TcpSessions => _tcpSessions;

    public SshPasswordCache PasswordCache { get; } = new();

    public ConfigImportResult? ImportResult { get; private set; }

    public IReadOnlyList<SerialPortInfo> Ports { get; private set; } = Array.Empty<SerialPortInfo>();

    public IReadOnlyDictionary<string, SerialPortInfo> PortMap { get; private set; } =
        new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

    public static AppConfigService Load()
    {
        var import = PythonConfigImporter.ImportIfMissing();
        var path = ConfigPaths.AvaloniaConfigFile();
        var config = ComtoolConfig.Load(path);
        var service = new AppConfigService(config, path)
        {
            ImportResult = import,
        };
        service.RefreshPorts();
        return service;
    }

    public SerialPortScanResult RefreshPorts()
    {
        var scan = SerialPortCatalog.Scan();
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

        var removed = _serialSessions.Prune(scan.Ports.Select(port => port.Device));
        if (removed.Count > 0)
        {
            Save();
        }

        return scan;
    }

    public void Save()
    {
        _serialSessions.Save(Config);
        _sshSessions.Save(Config);
        _tcpSessions.Save(Config);
        Config.Save(ConfigPath);
    }

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
}
