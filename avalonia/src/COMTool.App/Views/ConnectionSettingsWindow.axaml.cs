using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    private readonly AppConfigService _config;
    private readonly TerminalTab? _currentTab;
    private readonly Func<string, Task> _openSerial;
    private readonly Func<string, Task> _openSsh;
    private readonly Func<string, Task> _showError;

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
        Func<string, Task> openSerial,
        Func<string, Task> openSsh,
        Func<string, Task> showError)
    {
        _config = config;
        _currentTab = currentTab;
        _openSerial = openSerial;
        _openSsh = openSsh;
        _showError = showError;

        InitializeComponent();
        ConnectionTypeCombo.SelectionChanged += OnConnectionTypeChanged;
        ConnectionTypeCombo.SelectedIndex = 0;
        PopulateSerialDefaults();
        PopulateSshDefaults();
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

    private void PopulateCurrentSession()
    {
        if (_currentTab is null)
        {
            ConnectionTypeCombo.SelectedIndex = 0;
            UpdateConnectionPanels();
            return;
        }

        ConnectionTypeCombo.SelectedIndex = _currentTab.Kind == SessionKind.Ssh ? 1 : 0;
        UpdateConnectionPanels();
        Title = $"{_currentTab.Title} - Connection settings";

        if (_currentTab.Kind == SessionKind.Serial)
        {
            var session = _config.SerialSessions.Get(_currentTab.SessionId);
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
        else if (_currentTab.Kind == SessionKind.Ssh)
        {
            var session = _config.SshSessions.Get(_currentTab.SessionId);
            if (session is not null)
            {
                HostBox.Text = session.Host;
                SshPortBox.Text = session.Port.ToString();
                UserBox.Text = session.User;
                AliasBox.Text = session.Alias;
                KeyFileBox.Text = session.SshKey;
            }
        }
    }

    private void OnConnectionTypeChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateConnectionPanels();

    private void UpdateConnectionPanels()
    {
        var isSsh = ConnectionTypeCombo.SelectedIndex == 1;
        SerialPanel.IsVisible = !isSsh;
        SshPanel.IsVisible = isSsh;
    }

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
            if (ConnectionTypeCombo.SelectedIndex == 1)
            {
                await OpenSshAsync();
            }
            else
            {
                await OpenSerialAsync();
            }
        }
        catch (Exception ex)
        {
            var action = ConnectionTypeCombo.SelectedIndex == 1
                ? "Connect Failed"
                : "Open Failed";
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
        await _openSerial(port.Device);
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
        await _openSsh(session.Key);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
