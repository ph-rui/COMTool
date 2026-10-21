using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Views;

/// <summary>
/// Non-modal terminal connection settings, mirroring the Python dialog. The
/// dialog edits the remembered session store and then asks the main window to
/// open/reconnect the transport.
/// </summary>
public partial class ConnectionSettingsWindow : Window
{
    private static readonly string[] Baudrates =
    {
        "300", "600", "1200", "2400", "4800", "9600", "14400", "19200",
        "38400", "57600", "115200", "128000", "230400", "256000", "460800",
        "500000", "576000", "921600", "1000000", "1500000", "2000000",
    };

    private static readonly string[] FlowControls =
    {
        "None", "XON/XOFF", "RTS/CTS", "DSR/DTR",
    };

    private const string AllTcpClients = "All clients";

    private readonly AppConfigService _config;
    private readonly TerminalTab? _currentTab;
    private readonly SessionKind _initialKind;
    private readonly string? _sessionId;
    private readonly SessionKind _sessionKind;
    private readonly string? _sessionTitle;
    private readonly Func<string, TerminalTab?, Task> _openSerial;
    private readonly Func<string, TerminalTab?, Task> _openSsh;
    private readonly Func<string, TerminalTab?, Task> _openTelnet;
    private readonly Func<string, TerminalTab?, Task> _openTcp;
    private readonly Func<string, Task> _showError;
    private ITcpSession? _tcpSession;
    private bool _updatingTcpClients;

    // Avalonia's XAML runtime loader requires a public parameterless
    // constructor. The window is always created through the parameterized
    // constructor above.
    public ConnectionSettingsWindow()
    {
        throw new InvalidOperationException(
            "ConnectionSettingsWindow must be created with its configuration.");
    }

    public ConnectionSettingsWindow(
        AppConfigService config,
        TerminalTab? currentTab,
        Func<string, TerminalTab?, Task> openSerial,
        Func<string, TerminalTab?, Task> openSsh,
        Func<string, TerminalTab?, Task> openTelnet,
        Func<string, TerminalTab?, Task> openTcp,
        Func<string, Task> showError,
        SessionKind initialKind = SessionKind.Serial,
        string? editSessionId = null,
        string? editTitle = null)
    {
        _config = config;
        _currentTab = currentTab;
        _initialKind = initialKind;
        _sessionId = currentTab?.SessionId ?? editSessionId;
        _sessionKind = currentTab?.Kind ?? initialKind;
        _sessionTitle = currentTab?.Title ?? editTitle;
        _openSerial = openSerial;
        _openSsh = openSsh;
        _openTelnet = openTelnet;
        _openTcp = openTcp;
        _showError = showError;

        InitializeComponent();
        ConnectionTypeCombo.SelectionChanged += OnConnectionTypeChanged;
        ConnectionTypeCombo.SelectedIndex = 0;
        PopulateSerialDefaults();
        PopulateSshDefaults();
        PopulateTelnetDefaults();
        PopulateTcpDefaults();
        PopulateCurrentSession();
    }

    private void PopulateSerialDefaults()
    {
        var ports = _config.Ports.ToList();
        PortCombo.ItemsSource = ports;
        BaudrateCombo.ItemsSource = Baudrates;
        BaudrateCombo.Text = "115200";
        DataBytesCombo.ItemsSource = new[] { "5", "6", "7", "8" };
        DataBytesCombo.SelectedItem = "8";
        ParityCombo.ItemsSource = new[] { "None", "Odd", "Even", "Mark", "Space" };
        ParityCombo.SelectedItem = "None";
        StopBitsCombo.ItemsSource = new[] { "1", "1.5", "2" };
        StopBitsCombo.SelectedItem = "1";
        FlowControlCombo.ItemsSource = FlowControls;
        FlowControlCombo.SelectedItem = "None";
        RtsCheck.IsChecked = false;
        DtrCheck.IsChecked = false;
    }

    private void PopulateSshDefaults()
    {
        SshPortBox.Text = SshSessionRules.DefaultPort.ToString();
        AliasBox.Text = "";
        KeyFileBox.Text = "";
        PasswordBox.Text = "";
        RememberCheck.IsChecked = true;
    }

    private void PopulateTelnetDefaults()
    {
        TelnetPortBox.Text = TelnetSessionRules.DefaultPort.ToString();
        TelnetUserBox.Text = "";
        TelnetAliasBox.Text = "";
        TelnetPasswordBox.Text = "";
        TelnetRememberCheck.IsChecked = true;
    }

    private void PopulateTcpDefaults()
    {
        TcpProtocolCombo.SelectedIndex = 0;
        TcpModeCombo.SelectedIndex = 0;
        TcpTargetBox.Text = $"127.0.0.1:{TcpSessionRules.DefaultPort}";
        TcpPortBox.Text = TcpSessionRules.DefaultPort.ToString();
        TcpLocalBox.Text = "";
        TcpAutoReconnectCheck.IsChecked = false;
        TcpReconnectIntervalBox.Text = "1.0";
        TcpClientsCombo.ItemsSource = new[] { AllTcpClients };
        TcpClientsCombo.SelectedIndex = 0;
        TcpDisconnectButton.IsEnabled = false;
        UpdateTcpFields();
    }

    private void PopulateCurrentSession()
    {
        if (_sessionId is null)
        {
            ConnectionTypeCombo.SelectedIndex = _initialKind switch
            {
                SessionKind.Ssh => 1,
                SessionKind.Telnet => 2,
                SessionKind.Tcp => 3,
                _ => 0,
            };
            Title = "New session";
            UpdateConnectionPanels();
            return;
        }

        ConnectionTypeCombo.SelectedIndex = _sessionKind switch
        {
            SessionKind.Ssh => 1,
            SessionKind.Telnet => 2,
            SessionKind.Tcp => 3,
            _ => 0,
        };
        UpdateConnectionPanels();
        Title = $"{_sessionTitle ?? _sessionId} - Connection settings";

        if (_sessionKind == SessionKind.Serial)
        {
            var session = _config.SerialSessions.Get(_sessionId);
            if (session is not null)
            {
                var ports = (PortCombo.ItemsSource as IList<SerialPortInfo>)?.ToList()
                    ?? new List<SerialPortInfo>();
                var index = ports.FindIndex(port =>
                    string.Equals(port.Device, session.Port, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    ports.Add(new SerialPortInfo(session.Port, session.Name));
                    PortCombo.ItemsSource = ports;
                    index = ports.Count - 1;
                }

                PortCombo.SelectedIndex = index;
                BaudrateCombo.Text = session.Serial.Baudrate.ToString();
                DataBytesCombo.SelectedItem = session.Serial.Bytesize.ToString();
                ParityCombo.SelectedItem = session.Serial.Parity;
                StopBitsCombo.SelectedItem = session.Serial.Stopbits;
                FlowControlCombo.SelectedItem = session.Serial.Flowcontrol;
                RtsCheck.IsChecked = session.Serial.Rts;
                DtrCheck.IsChecked = session.Serial.Dtr;
            }
        }
        else if (_sessionKind == SessionKind.Ssh)
        {
            var session = _config.SshSessions.Get(_sessionId);
            if (session is not null)
            {
                HostBox.Text = session.Host;
                SshPortBox.Text = session.Port.ToString();
                UserBox.Text = session.User;
                AliasBox.Text = session.Alias;
                KeyFileBox.Text = session.SshKey;
            }
        }
        else if (_sessionKind == SessionKind.Telnet)
        {
            var session = _config.TelnetSessions.Get(_sessionId);
            if (session is not null)
            {
                TelnetHostBox.Text = session.Host;
                TelnetPortBox.Text = session.Port.ToString();
                TelnetUserBox.Text = session.User;
                TelnetAliasBox.Text = session.Alias;
            }
        }
        else if (_sessionKind == SessionKind.Tcp)
        {
            var session = _config.TcpSessions.Get(_sessionId);
            if (session is not null)
            {
                TcpProtocolCombo.SelectedIndex =
                    TcpSessionRules.NormalizeProtocol(session.Protocol) == TcpSessionRules.UdpProtocol
                        ? 1
                        : 0;
                TcpModeCombo.SelectedIndex =
                    TcpSessionRules.NormalizeMode(session.Mode) == TcpSessionRules.ServerMode
                        ? 1
                        : 0;
                TcpTargetBox.Text = session.Target;
                TcpPortBox.Text = session.Port.ToString();
                TcpAutoReconnectCheck.IsChecked = session.AutoReconnect;
                TcpReconnectIntervalBox.Text = session.ReconnectInterval.ToString("0.###");
            }

            if (_currentTab?.Connection is ITcpSession tcp)
            {
                _tcpSession = tcp;
                tcp.ClientsChanged += OnTcpClientsChanged;
                RefreshTcpClients();
            }

            UpdateTcpFields();
        }
    }

    private void OnConnectionTypeChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateConnectionPanels();

    private void OnTcpModeChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateTcpFields();

    private void UpdateConnectionPanels()
    {
        var index = ConnectionTypeCombo.SelectedIndex;
        SerialPanel.IsVisible = index == 0;
        SshPanel.IsVisible = index == 1;
        TelnetPanel.IsVisible = index == 2;
        TcpPanel.IsVisible = index == 3;
        UpdateTcpFields();
    }

    private void UpdateTcpFields()
    {
        var isUdp = TcpProtocolCombo.SelectedIndex == 1;
        var isServer = TcpModeCombo.SelectedIndex == 1;
        TcpTargetLabel.IsVisible = !isServer;
        TcpTargetBox.IsVisible = !isServer;
        TcpPortLabel.IsVisible = isServer;
        TcpPortBox.IsVisible = isServer;
        TcpPortLabel.Text = isUdp ? "Local port" : "Port";
        TcpLocalLabel.IsVisible = !isUdp && !isServer;
        TcpLocalBox.IsVisible = !isUdp && !isServer;
        TcpClientsLabel.IsVisible = !isUdp && isServer;
        TcpClientsCombo.IsVisible = !isUdp && isServer;
        TcpDisconnectButton.IsVisible = !isUdp && isServer;
        TcpAutoReconnectLabel.IsVisible = !isUdp && !isServer;
        TcpAutoReconnectCheck.IsVisible = !isUdp && !isServer;
        TcpReconnectIntervalLabel.IsVisible = !isUdp && !isServer;
        TcpReconnectIntervalBox.IsVisible = !isUdp && !isServer;
    }

    private void OnTcpProtocolChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateTcpFields();

    private async void OnBrowseKeyClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SSH key",
            AllowMultiple = false,
        });
        if (files.Count > 0)
        {
            KeyFileBox.Text = files[0].Path.LocalPath;
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            switch (ConnectionTypeCombo.SelectedIndex)
            {
                case 1:
                    await OpenSshAsync();
                    break;
                case 2:
                    await OpenTelnetAsync();
                    break;
                case 3:
                    await OpenTcpAsync();
                    break;
                default:
                    await OpenSerialAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            var action = ConnectionTypeCombo.SelectedIndex switch
            {
                1 or 2 => "Connect Failed",
                3 => TcpProtocolCombo.SelectedIndex == 1 || TcpModeCombo.SelectedIndex == 1
                    ? "Open Failed"
                    : "Connect Failed",
                _ => "Open Failed",
            };
            await _showError($"{action}\n{ex.Message}");
        }
    }

    private async Task OpenSerialAsync()
    {
        if (PortCombo.SelectedItem is not SerialPortInfo port || port.Device.Length == 0)
        {
            throw new InvalidOperationException("Select a serial port first.");
        }

        if (!int.TryParse(BaudrateCombo.Text?.Trim(), out var baudrate) || baudrate <= 0)
        {
            throw new InvalidOperationException("Baudrate must be a positive integer.");
        }

        var serial = new SerialConnectionConfig
        {
            Port = port.Device,
            Baudrate = baudrate,
            Bytesize = int.Parse(DataBytesCombo.SelectedItem?.ToString() ?? "8"),
            Parity = ParityCombo.SelectedItem?.ToString() ?? "None",
            Stopbits = StopBitsCombo.SelectedItem?.ToString() ?? "1",
            Flowcontrol = FlowControlCombo.SelectedItem?.ToString() ?? "None",
            Rts = RtsCheck.IsChecked == true,
            Dtr = DtrCheck.IsChecked == true,
        };

        _config.SerialSessions.Upsert(port.Device, serial);
        _config.Save();
        Close();
        await _openSerial(port.Device, _currentTab);
    }

    private async Task OpenSshAsync()
    {
        var host = HostBox.Text?.Trim() ?? "";
        if (host.Length == 0)
        {
            throw new InvalidOperationException("Host must not be empty.");
        }

        if (!int.TryParse(SshPortBox.Text?.Trim(), out var port) || port <= 0 || port > 65535)
        {
            throw new InvalidOperationException("SSH port must be between 1 and 65535.");
        }

        var user = UserBox.Text?.Trim() ?? "";
        if (user.Length == 0)
        {
            throw new InvalidOperationException("User must not be empty.");
        }

        var keyFile = KeyFileBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        var remember = RememberCheck.IsChecked == true;

        var session = _config.SshSessions.Upsert(
            host,
            port,
            user,
            alias: AliasBox.Text?.Trim() ?? "",
            sshKey: keyFile);
        if (session is null)
        {
            throw new InvalidOperationException("Could not save the SSH session.");
        }

        if (password.Length > 0)
        {
            _config.PasswordCache.Set(session.Key, password);
            if (remember)
            {
                _config.RememberSshPassword(session, password, remember: true);
            }
        }

        _config.Save();
        Close();
        await _openSsh(session.Key, _currentTab);
    }

    private async Task OpenTelnetAsync()
    {
        var host = TelnetHostBox.Text?.Trim() ?? "";
        if (host.Length == 0)
        {
            throw new InvalidOperationException("Host must not be empty.");
        }

        if (!int.TryParse(TelnetPortBox.Text?.Trim(), out var port) || port <= 0 || port > 65535)
        {
            throw new InvalidOperationException("Telnet port must be between 1 and 65535.");
        }

        var password = TelnetPasswordBox.Text ?? "";
        var remember = TelnetRememberCheck.IsChecked == true;
        var session = _config.TelnetSessions.Upsert(
            host,
            port,
            TelnetUserBox.Text?.Trim() ?? "",
            alias: TelnetAliasBox.Text?.Trim() ?? "");
        if (session is null)
        {
            throw new InvalidOperationException("Could not save the Telnet session.");
        }

        if (password.Length > 0)
        {
            _config.PasswordCache.Set(session.Key, password);
            if (remember)
            {
                _config.RememberTelnetPassword(session, password, remember: true);
            }
        }

        _config.Save();
        Close();
        await _openTelnet(session.Key, _currentTab);
    }

    private async Task OpenTcpAsync()
    {
        var isUdp = TcpProtocolCombo.SelectedIndex == 1;
        var isServer = TcpModeCombo.SelectedIndex == 1;
        var protocol = isUdp ? TcpSessionRules.UdpProtocol : TcpSessionRules.TcpProtocol;
        var mode = isServer ? TcpSessionRules.ServerMode : TcpSessionRules.ClientMode;
        var target = "";
        int port;

        if (isUdp)
        {
            if (!TcpTarget.TryParse(TcpTargetBox.Text, out var parsed) || parsed is null)
            {
                throw new InvalidOperationException("Target must be host[:port].");
            }

            if (!int.TryParse(TcpPortBox.Text?.Trim(), out port)
                || !TcpSessionRules.IsValidPort(port))
            {
                throw new InvalidOperationException("Local port must be between 1 and 65535.");
            }

            target = parsed.ToString();
        }
        else if (isServer)
        {
            if (!int.TryParse(TcpPortBox.Text?.Trim(), out port)
                || !TcpSessionRules.IsValidPort(port))
            {
                throw new InvalidOperationException("Port must be between 1 and 65535.");
            }
        }
        else
        {
            if (!TcpTarget.TryParse(TcpTargetBox.Text, out var parsed) || parsed is null)
            {
                throw new InvalidOperationException("Target must be host[:port].");
            }

            target = parsed.ToString();
            port = parsed.Port;
        }

        var autoReconnect = false;
        var reconnectInterval = 1.0;
        if (!isUdp && !isServer)
        {
            autoReconnect = TcpAutoReconnectCheck.IsChecked == true;
            if (!double.TryParse(TcpReconnectIntervalBox.Text?.Trim(), out reconnectInterval)
                || reconnectInterval <= 0)
            {
                throw new InvalidOperationException("Reconnect interval must be a positive number of seconds.");
            }
        }

        var session = _config.TcpSessions.Upsert(
            protocol,
            mode,
            target,
            port,
            autoReconnect,
            reconnectInterval);
        if (session is null)
        {
            throw new InvalidOperationException("Could not save the TCP session.");
        }

        _config.Save();
        Close();
        await _openTcp(session.Key, _currentTab);
    }

    private void OnTcpClientsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RefreshTcpClients);

    private void RefreshTcpClients()
    {
        if (_tcpSession is null)
        {
            return;
        }

        _updatingTcpClients = true;
        try
        {
            var previous = TcpClientsCombo.SelectedItem as string;
            var items = new List<string> { AllTcpClients };
            items.AddRange(_tcpSession.Clients.Select(client => client.Endpoint));
            TcpClientsCombo.ItemsSource = items;
            var index = previous is null ? 0 : items.IndexOf(previous);
            TcpClientsCombo.SelectedIndex = index >= 0 ? index : 0;
            TcpLocalBox.Text = _tcpSession.LocalEndpoint;
            TcpDisconnectButton.IsEnabled = _tcpSession.Clients.Count > 0;
        }
        finally
        {
            _updatingTcpClients = false;
        }
    }

    private async void OnTcpClientSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingTcpClients || _tcpSession is null)
        {
            return;
        }

        try
        {
            var selected = TcpClientsCombo.SelectedItem as string;
            _tcpSession.SelectClient(
                selected is null || selected == AllTcpClients ? null : selected);
        }
        catch (Exception ex)
        {
            await _showError($"Select Failed\n{ex.Message}");
        }
    }

    private async void OnTcpDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (_tcpSession is null)
        {
            return;
        }

        try
        {
            var selected = TcpClientsCombo.SelectedItem as string;
            _tcpSession.DisconnectClient(
                selected is null || selected == AllTcpClients ? null : selected);
            RefreshTcpClients();
        }
        catch (Exception ex)
        {
            await _showError($"Disconnect Failed\n{ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_tcpSession is not null)
        {
            _tcpSession.ClientsChanged -= OnTcpClientsChanged;
        }

        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
