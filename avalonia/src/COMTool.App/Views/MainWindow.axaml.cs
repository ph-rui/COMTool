using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Connections;
using COMTool.Core.Config;
using COMTool.Zmodem;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

public partial class MainWindow : Window
{
    private static readonly Cursor WestEastCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouthCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestSouthEastCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastSouthWestCursor = new(StandardCursorType.TopRightCorner);
    private bool _sidebarVisible = true;
    private double _sidebarWidth = 220;
    private bool _loaded;
    private bool _applyingEncoding;
    private AppConfigService? _config;
    private DispatcherTimer? _portTimer;
    private readonly DispatcherTimer _sessionHoverTimer;
    private bool _sessionHoverSeen;
    private bool _windowPointerPressed;
    private double _lastSessionPointerX;
    private double _lastSessionPointerY;
    private double _lastSessionTimerX;
    private DateTime _sessionShownAt = DateTime.MinValue;
    private ConnectionSettingsWindow? _settingsWindow;
    private readonly Dictionary<TerminalTab, ZmodemTiming> _zmodemTimings = new();
    private readonly Dictionary<TerminalTab, ZmodemStream> _zmodemStreams = new();
    private readonly Dictionary<TerminalTab, DetachedTabState> _detachedTabs = new();
    private static readonly IBrush StatusInfoBrush =
        new SolidColorBrush(Color.Parse("#008200"));
    private static readonly IBrush StatusWarningBrush =
        new SolidColorBrush(Color.Parse("#e65100"));
    private static readonly IBrush StatusErrorBrush =
        new SolidColorBrush(Color.Parse("#c62828"));
    private static readonly IBrush StatusProgressBrush =
        new SolidColorBrush(Color.Parse("#1565c0"));
    private const double DefaultWindowWidth = 850;
    private const double DefaultWindowHeight = 500;
    private const double MinimumWindowWidth = 500;
    private const double MinimumWindowHeight = 320;
    private double _lastNormalWidth = DefaultWindowWidth;
    private double _lastNormalHeight = DefaultWindowHeight;

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeIcon();
        SizeChanged += OnWindowSizeChanged;
        ApplySavedWindowSizeBeforeShow();
        _sessionHoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _sessionHoverTimer.Tick += (_, _) => CheckSessionSidebarAutoHide();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(
            PointerCaptureLostEvent,
            OnWindowPointerCaptureLost,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Sidebar.SessionActivated += OnSessionActivated;
        Sidebar.RemoveRequested += OnSessionRemoveRequested;
        Opened += OnOpened;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeIcon();
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_loaded || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        if (Environment.GetCommandLineArgs().Contains("--demo"))
        {
            viewModel.LoadDemoSessions();
        }
        else
        {
            try
            {
                _config = AppConfigService.Load();
                AppLog.Info($"Config loaded: {_config.ConfigPath}");
                ThemeManager.Apply(_config.Config.Skin);
                ApplyEncodingConfig();
                ApplySidebarConfig();
                ApplyWindowSizeConfig();
                RefreshSidebar();
                _portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _portTimer.Tick += (_, _) => RefreshPorts();
                _portTimer.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to load config", ex);
                _ = ShowErrorAsync($"Failed to load config: {ex.Message}");
            }
        }

        if (Environment.GetCommandLineArgs().Contains("--terminal"))
        {
            AddLocalTerminal();
        }
    }

    private void RefreshPorts()
    {
        if (_config is null)
        {
            return;
        }

        var previous = _config.Ports
            .Select(port => port.Device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scan = _config.RefreshPorts();
        if (!scan.Success)
        {
            // A failed scan means "do not prune remembered sessions", not
            // "stop watching live tabs". Connection state is independent of
            // port enumeration.
            UpdateConnectionStates();
            return;
        }

        var current = scan.Ports
            .Select(port => port.Device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!previous.SetEquals(current))
        {
            RefreshSidebar();
        }
        else
        {
            UpdateConnectionStates();
        }
    }

    private (HashSet<string> Serial, HashSet<string> Ssh) ConnectedSessions()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var serial = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Serial && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ssh = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Ssh && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (serial, ssh);
    }

    private void UpdateConnectionStates()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        foreach (var tab in AllTabs())
        {
            if (!tab.IsAlive || tab.IsTransportOpen is null)
            {
                continue;
            }

            var connected = false;
            try
            {
                connected = tab.IsTransportOpen();
            }
            catch
            {
                // A disposed SSH client or serial port is simply disconnected.
            }

            if (!connected && tab.StatusMessage != "Connection lose")
            {
                tab.StatusMessage = "Connection lose";
                tab.StatusBrush = StatusWarningBrush;
                tab.TabDotBrush = StatusWarningBrush;
            }
        }

        var (serial, ssh) = ConnectedSessions();
        viewModel.UpdateConnectionStates(serial, ssh);
    }

    private void RefreshSidebar()
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var (serial, ssh) = ConnectedSessions();
        viewModel.LoadSessions(_config, serial, ssh);
    }

    private async void OnSessionActivated(object? sender, SessionItem item)
    {
        if (DataContext is not MainViewModel viewModel || _config is null)
        {
            return;
        }

        var detached = FindDetachedTab(item);
        if (detached is not null)
        {
            if (IsTabConnected(detached))
            {
                _detachedTabs[detached].Window.Activate();
                detached.Content.Focus();
                return;
            }

            _detachedTabs[detached].SuppressReattach = true;
            _detachedTabs[detached].Window.Close();
        }

        var existing = viewModel.FindTab(item.SessionId, item.Kind);
        if (existing is not null && IsTabConnected(existing))
        {
            viewModel.SelectedTab = existing;
            existing.Content.Focus();
            return;
        }

        if (existing is not null)
        {
            CloseTab(existing);
        }

        try
        {
            if (item.Kind == SessionKind.Serial)
            {
                OpenSerialSession(item.SessionId);
            }
            else if (item.Kind == SessionKind.Ssh)
            {
                await OpenSshSessionAsync(item.SessionId);
            }
        }
        catch (Exception ex)
        {
            var action = item.Kind == SessionKind.Serial
                ? "Open Failed"
                : "Connect Failed";
            await ShowErrorAsync($"{action}\n{ex.Message}");
        }
    }

    private async void OnSessionRemoveRequested(object? sender, SessionItem item)
    {
        if (_config is null || DataContext is not MainViewModel)
        {
            return;
        }

        string label;
        string kind;
        if (item.Kind == SessionKind.Ssh)
        {
            var session = _config.SshSessions.Get(item.SessionId);
            label = session is null
                ? item.SessionId
                : $"{SshSessionRules.DisplayName(session)} ({SshSessionRules.Detail(session)})";
            kind = "SSH";
        }
        else
        {
            label = item.SessionId;
            kind = "serial";
        }

        var confirmed = await ShowConfirmAsync(
            "Remove session",
            $"Remove this {kind} session from memory?\n{label}");
        if (!confirmed)
        {
            return;
        }

        if (item.Kind == SessionKind.Ssh)
        {
            _config.SshSessions.Remove(item.SessionId);
            _config.PasswordCache.Remove(item.SessionId);
        }
        else
        {
            _config.SerialSessions.Remove(item.SessionId);
        }

        _config.Save();
        RefreshSidebar();
    }

    private void OpenSerialSession(string port)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.SerialSessions.Get(port);
        if (session is null)
        {
            throw new InvalidOperationException($"Serial session {port} was not found.");
        }

        var connection = SerialConnection.Open(session.Serial);
        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = session.Port,
            SessionId = session.Port,
            Kind = SessionKind.Serial,
            Content = terminal,
            Connection = connection,
            StatusMessage = $"Connected {session.Port}",
            IsTransportOpen = () => connection.IsOpen && !connection.Pty.IsInputCompleted,
            CloseAction = () =>
            {
                try
                {
                    terminal.DetachConnection();
                }
                catch
                {
                    // The template may not have been applied if opening failed.
                }

                connection.Dispose();
            },
        };
        SubscribeZmodem(tab, connection.Pty.Zmodem);

        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached)
                    {
                        return;
                    }

                    terminalAttached = true;
                    AttachTerminal(tab, terminal, connection.Pty);
                },
                DispatcherPriority.Loaded);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            tab.IsAlive = false;
            tab.StatusMessage = "Connection lose";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            UpdateConnectionStates();
        });

        viewModel.AddTab(tab);
        UpdateConnectionStates();
        terminal.Focus();
    }

    private async Task OpenSshSessionAsync(string key)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.SshSessions.Get(key);
        if (session is null)
        {
            throw new InvalidOperationException($"SSH session {key} was not found.");
        }

        string? password = null;
        var remember = false;
        if (string.IsNullOrWhiteSpace(session.SshKey))
        {
            password = _config.PasswordCache.Get(session.Key);
            if (string.IsNullOrEmpty(password))
            {
                password = SecretProtector.Unprotect(session.EncryptedPassword);
            }
        }

        SshConnection connection;
        for (var attempt = 0; ; attempt++)
        {
            if (string.IsNullOrWhiteSpace(session.SshKey) && string.IsNullOrEmpty(password))
            {
                var dialog = new SshPasswordDialog(SshSessionRules.Detail(session));
                var accepted = await dialog.ShowDialog<bool>(this);
                if (!accepted)
                {
                    return;
                }

                password = dialog.Password;
                remember = dialog.Remember;
            }

            try
            {
                connection = SshConnection.Connect(new SshConnectionOptions(
                    session.Host,
                    session.Port,
                    session.User,
                    Password: password,
                    KeyFile: session.SshKey,
                    Columns: 100,
                    Rows: 30));
                break;
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
                when (string.IsNullOrWhiteSpace(session.SshKey) && attempt < 2)
            {
                _config.PasswordCache.Remove(session.Key);
                password = null;
                remember = false;
            }
        }

        _config.PasswordCache.Set(session.Key, password);
        if (remember && !string.IsNullOrEmpty(password))
        {
            _config.RememberSshPassword(session, password, remember: true);
        }

        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = SshSessionRules.DisplayName(session),
            SessionId = session.Key,
            Kind = SessionKind.Ssh,
            Content = terminal,
            Connection = connection,
            StatusMessage = $"Connected {SshSessionRules.Detail(session)}",
            IsTransportOpen = () => connection.IsConnected,
            CloseAction = () =>
            {
                try
                {
                    terminal.DetachConnection();
                }
                catch
                {
                    // The template may not have been applied if opening failed.
                }

                connection.Dispose();
            },
        };

        SubscribeZmodem(tab, connection.Pty.Zmodem);
        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached)
                    {
                        return;
                    }

                    terminalAttached = true;
                    AttachTerminal(tab, terminal, connection.Pty);
                },
                DispatcherPriority.Loaded);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            tab.IsAlive = false;
            tab.StatusMessage = "Connection lose";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            UpdateConnectionStates();
        });

        viewModel.AddTab(tab);
        UpdateConnectionStates();
        terminal.Focus();
    }

    private static bool IsTabConnected(TerminalTab tab) =>
        tab.IsAlive && (tab.IsTransportOpen?.Invoke() ?? true);

    private void AttachTerminal(
        TerminalTab tab,
        TerminalControl terminal,
        PtyStreamConnection pty)
    {
        if (!tab.IsAlive)
        {
            return;
        }

        try
        {
            terminal.AttachConnection(pty);
        }
        catch (Exception ex)
        {
            tab.IsAlive = false;
            _ = ShowErrorAsync($"Failed to attach terminal: {ex.Message}");
        }
    }

    private static FontFamily TerminalFontFamily()
    {
        // Avalonia treats the comma-separated Qt family list as one name and
        // falls back to a proportional UI font, which gives every cell the
        // width of a wide fallback glyph. Pick a real monospace family.
        if (OperatingSystem.IsWindows())
        {
            return new FontFamily("Consolas");
        }

        if (OperatingSystem.IsMacOS())
        {
            return new FontFamily("Menlo");
        }

        return new FontFamily("DejaVu Sans Mono");
    }

    private TerminalControl CreateTerminal()
    {
        var terminal = new TerminalControl
        {
            // The control defaults Process to bash/cmd and auto-launches it when
            // loaded. These tabs always attach an external serial/SSH stream, so
            // an empty Process stops a local shell from starting on reparent and
            // writing its own prompt into the terminal buffer.
            Process = string.Empty,
            FontFamily = TerminalFontFamily(),
            FontSize = 15,
            Background = new SolidColorBrush(Color.Parse("#212121")),
            Foreground = Brushes.White,
            CursorColor = Color.Parse("#4caf50"),
            BufferSize = 9999,
        };
        if (terminal.Options is { } terminalOptions)
        {
            terminalOptions.RightClickSelectsWord = false;
        }

        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () => HideTerminalScrollBars(terminal),
                DispatcherPriority.Loaded);
        terminal.PointerPressed += OnTerminalPointerPressed;
        terminal.KeyDown += OnTerminalKeyDown;
        return terminal;
    }

    private static void HideTerminalScrollBars(TerminalControl terminal)
    {
        foreach (var descendant in terminal.GetVisualDescendants())
        {
            if (descendant is not ScrollBar scrollBar)
            {
                continue;
            }

            scrollBar.IsVisible = false;
            scrollBar.Width = 0;
            scrollBar.MinWidth = 0;
        }
    }

    private void SubscribeZmodem(TerminalTab tab, ZmodemStream? zmodem)
    {
        if (zmodem is null)
        {
            return;
        }

        _zmodemStreams[tab] = zmodem;
        zmodem.SendRequested += request =>
            Dispatcher.UIThread.Post(() =>
            {
                tab.IsZmodemActive = true;
                tab.IsZmodemIndeterminate = true;
                tab.IsZmodemCancellable = true;
                tab.ZmodemLabel = "Select files to send...";
                tab.ZmodemPercentText = "";
                tab.ZmodemProgress = 0;
                _ = PromptZmodemSendAsync(tab, zmodem);
            });
        zmodem.Progress += progress => Dispatcher.UIThread.Post(() =>
        {
            var percent = progress.Total is > 0
                ? (int)Math.Clamp(progress.Sent * 100 / progress.Total.Value, 0, 100)
                : 0;
            if (!_zmodemTimings.TryGetValue(tab, out var timing)
                || !string.Equals(timing.Name, progress.Name, StringComparison.Ordinal))
            {
                timing = new ZmodemTiming(progress.Name, DateTime.UtcNow, progress.Sent);
                _zmodemTimings[tab] = timing;
            }

            var elapsed = Math.Max(
                0.001,
                (DateTime.UtcNow - timing.StartedAt).TotalSeconds);
            var rate = Math.Max(0, progress.Sent - timing.StartBytes) / elapsed;
            var detail = rate > 0 ? $" {FormatRate(rate)}" : "";
            if (progress.Total is > 0 && rate > 0 && progress.Total.Value > progress.Sent)
            {
                detail += $" ETA {FormatEta((progress.Total.Value - progress.Sent) / rate)}";
            }

            tab.IsZmodemActive = true;
            tab.IsZmodemIndeterminate = progress.Total is not > 0;
            tab.IsZmodemCancellable = true;
            tab.ZmodemLabel = progress.Name;
            tab.ZmodemProgress = percent;
            tab.ZmodemPercentText = $"{percent}%";
            tab.StatusMessage =
                $"{(progress.IsSend ? "Sending" : "Receiving")} {progress.Name} {percent}%{detail}";
            tab.StatusBrush = StatusProgressBrush;
        });
        zmodem.TransferCompleted += result =>
            Dispatcher.UIThread.Post(() => { _ = CompleteZmodemAsync(tab, result); });
        zmodem.TransferFailed += message => Dispatcher.UIThread.Post(() =>
        {
            _zmodemTimings.Remove(tab);
            tab.IsZmodemActive = false;
            tab.StatusMessage = $"ZMODEM error: {message}";
            tab.StatusBrush = StatusErrorBrush;
        });
        zmodem.TransferCancelled += () => Dispatcher.UIThread.Post(() =>
        {
            _zmodemTimings.Remove(tab);
            tab.IsZmodemActive = false;
            tab.StatusMessage = "File transfer cancelled";
            tab.StatusBrush = StatusWarningBrush;
        });
    }

    private async Task PromptZmodemSendAsync(TerminalTab tab, ZmodemStream zmodem)
    {
        if (!tab.IsAlive)
        {
            zmodem.CancelSend();
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to send",
            AllowMultiple = true,
        });
        var paths = files
            .Select(file => file.Path.LocalPath)
            .Where(File.Exists)
            .ToArray();
        if (paths.Length == 0 || !tab.IsZmodemActive)
        {
            zmodem.CancelSend();
            tab.IsZmodemActive = false;
            tab.StatusMessage = "File transfer cancelled";
            tab.StatusBrush = StatusWarningBrush;
            return;
        }

        tab.IsZmodemActive = true;
        tab.IsZmodemIndeterminate = true;
        tab.IsZmodemCancellable = true;
        tab.ZmodemLabel = "Sending...";
        tab.ZmodemPercentText = "";
        tab.ZmodemProgress = 0;
        tab.StatusMessage = "Sending...";
        tab.StatusBrush = StatusProgressBrush;
        zmodem.StartSend(paths);
    }

    private async Task CompleteZmodemAsync(TerminalTab tab, ZmodemTransferResult result)
    {
        _zmodemTimings.Remove(tab);
        tab.IsZmodemActive = false;
        if (result.IsSend)
        {
            tab.StatusMessage = "Files sent";
            tab.StatusBrush = StatusInfoBrush;
            return;
        }

        if (!tab.IsAlive)
        {
            TryDeleteDirectory(result.TempDirectory);
            return;
        }

        var saved = 0;
        foreach (var received in result.Files)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save received file",
                SuggestedFileName = received.Name,
                DefaultExtension = Path.GetExtension(received.Name).TrimStart('.'),
            });
            if (file is null)
            {
                TryDeleteFile(received.Path);
                continue;
            }

            try
            {
                var target = file.Path.LocalPath;
                File.Move(received.Path, target, overwrite: true);
                saved++;
            }
            catch (Exception ex)
            {
                tab.StatusMessage = $"Save failed: {ex.Message}";
                tab.StatusBrush = StatusErrorBrush;
            }
        }

        TryDeleteDirectory(result.TempDirectory);
        tab.StatusMessage = $"Received {saved} file(s)";
        tab.StatusBrush = StatusInfoBrush;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private void AddLocalTerminal()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = "terminal",
            SessionId = $"terminal:{Guid.NewGuid():N}",
            Kind = SessionKind.Local,
            Content = terminal,
            StatusMessage = "Connected terminal",
            CloseAction = () => terminal.Kill(),
        };
        var launched = false;
        terminal.AttachedToVisualTree += (_, _) =>
        {
            if (launched || !tab.IsAlive)
            {
                return;
            }

            launched = true;
            try
            {
                terminal.LaunchProcess();
            }
            catch (Exception ex)
            {
                launched = false;
                tab.IsAlive = false;
                _ = ShowErrorAsync($"Failed to start terminal: {ex.Message}");
            }
        };

        viewModel.AddTab(tab);
        terminal.Focus();
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: TerminalTab tab } && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedTab = tab;
            tab.Content.Focus();
        }
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TerminalTab tab })
        {
            e.Handled = true;
            CloseTab(tab);
        }
    }

    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { Tag: TerminalTab tab }
            || DataContext is not MainViewModel viewModel
            || viewModel.Tabs.IndexOf(tab) < 0)
        {
            return;
        }

        if (!tab.IsAlive
            || (e.Source is Control source
                && source.FindAncestorOfType<Button>(includeSelf: true) is not null))
        {
            return;
        }

        // Removing the control from the visual tree must not be treated as the
        // end of its process. The terminal library has an explicit reparent
        // handshake for exactly this case.
        (tab.Content as TerminalControl)?.BeginReparent();
        var index = viewModel.RemoveTabForDetach(tab);
        if (index < 0)
        {
            (tab.Content as TerminalControl)?.EndReparent();
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(
            () => ShowDetachedTab(tab, index),
            DispatcherPriority.Loaded);
    }

    private void ShowDetachedTab(TerminalTab tab, int index)
    {
        if (!tab.IsAlive)
        {
            // The connection can drop between the double-click and this post.
            // Keep the tab in the strip instead of losing it.
            (tab.Content as TerminalControl)?.EndReparent();
            if (DataContext is MainViewModel deadViewModel)
            {
                deadViewModel.InsertDetachedTab(tab, index);
            }

            return;
        }

        Window? window = null;
        try
        {
            window = new Window
            {
                Title = tab.Title,
                Width = 850,
                Height = 500,
                MinWidth = 500,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Background,
                FontFamily = FontFamily,
                FontSize = FontSize,
                Icon = Icon,
                Content = tab.Content,
            };
            var state = new DetachedTabState(window, index);
            _detachedTabs[tab] = state;
            // Closing fires while the terminal is still in this window's visual
            // tree, before Avalonia detaches it. Suppress the terminal library's
            // implicit process cleanup for the trip back to the main window.
            window.Closing += (_, _) => (tab.Content as TerminalControl)?.BeginReparent();
            window.Closed += (_, _) => OnDetachedTabClosed(tab, state);
            window.Show();
            // Multiple detached windows otherwise land exactly on top of each
            // other, hiding every window but the last one.
            var cascade = Math.Max(0, _detachedTabs.Count - 1);
            if (cascade > 0)
            {
                const int offset = 24;
                window.Position = new PixelPoint(
                    window.Position.X + (offset * cascade),
                    window.Position.Y + (offset * cascade));
            }

            (tab.Content as TerminalControl)?.EndReparent();
            tab.Content.Focus();
        }
        catch (Exception ex)
        {
            _detachedTabs.Remove(tab);
            if (window is not null)
            {
                window.Content = null;
                window.Close();
            }

            (tab.Content as TerminalControl)?.EndReparent();
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.InsertDetachedTab(tab, index);
            }

            _ = ShowErrorAsync($"Failed to detach tab: {ex.Message}");
        }
    }

    private void OnDetachedTabClosed(TerminalTab tab, DetachedTabState state)
    {
        if (!_detachedTabs.Remove(tab))
        {
            return;
        }

        if (state.SuppressReattach || !tab.IsAlive)
        {
            DisposeDetachedTab(tab);
            return;
        }

        (tab.Content as TerminalControl)?.BeginReparent();
        state.Window.Content = null;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.InsertDetachedTab(tab, state.Index);
            (tab.Content as TerminalControl)?.EndReparent();
            tab.Content.Focus();
        }

        UpdateConnectionStates();
    }

    private void DisposeDetachedTab(TerminalTab tab)
    {
        try
        {
            tab.CloseAction?.Invoke();
        }
        catch
        {
            // The transport may already be gone with its detached window.
        }

        tab.IsAlive = false;
        tab.IsZmodemActive = false;
        _zmodemTimings.Remove(tab);
        _zmodemStreams.Remove(tab);
    }

    private TerminalTab? FindDetachedTab(SessionItem item)
    {
        foreach (var tab in _detachedTabs.Keys)
        {
            if (tab.Kind == item.Kind
                && string.Equals(tab.SessionId, item.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private IEnumerable<TerminalTab> AllTabs()
    {
        if (DataContext is MainViewModel viewModel)
        {
            foreach (var tab in viewModel.Tabs)
            {
                yield return tab;
            }
        }

        foreach (var tab in _detachedTabs.Keys)
        {
            yield return tab;
        }
    }

    private void CloseTab(TerminalTab tab)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            tab.CloseAction?.Invoke();
        }
        catch
        {
            // Closing a half-open transport must not keep the tab alive.
        }

        tab.IsAlive = false;
        tab.IsZmodemActive = false;
        _zmodemTimings.Remove(tab);
        _zmodemStreams.Remove(tab);
        viewModel.CloseTab(tab);
        UpdateConnectionStates();
        RestoreTerminalFocus();
    }

    private void OnZmodemCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || viewModel.SelectedTab is not { } tab
            || !_zmodemStreams.TryGetValue(tab, out var zmodem))
        {
            return;
        }

        tab.IsZmodemCancellable = false;
        tab.ZmodemLabel = "Cancelling...";
        zmodem.CancelSend();
        zmodem.CancelTransfer();
    }

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 190,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yes = new Button
        {
            Content = "Yes",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        yes.Click += (_, _) => dialog.Close(true);
        var no = new Button
        {
            Content = "No",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 190,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                ok,
            },
        };
        await dialog.ShowDialog(this);
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new Window
        {
            Title = "Error",
            Width = 440,
            Height = 190,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                ok,
            },
        };
        await dialog.ShowDialog(this);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (e.Source is Control toggleSource && toggleSource.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null)
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinIcon.Text = Topmost ? "\uf9f7" : "\uf670";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11)
        {
            return;
        }

        // Python's title-bar mixin treats F11 as a full-screen toggle and
        // hides the title bar while full screen is active.
        var fullScreen = WindowState == WindowState.FullScreen;
        WindowState = fullScreen ? WindowState.Normal : WindowState.FullScreen;
        TitleBar.IsVisible = fullScreen;
        RootGrid.RowDefinitions[0].Height = new GridLength(fullScreen ? 35 : 0);
        e.Handled = true;
    }

    private void OnLightThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("light");

    private void OnDarkThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("dark");

    private async void OnAboutClick(object? sender, RoutedEventArgs e) =>
        await ShowAboutAsync();

    private async Task ShowAboutAsync()
    {
        var dialog = new Window
        {
            Title = "About COMTool",
            Width = 560,
            Height = 430,
            MinWidth = 420,
            MinHeight = 320,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var links = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        links.Children.Add(LinkButton("GitHub", "https://github.com/Neutree/COMTool"));
        links.Children.Add(LinkButton(
            "Releases",
            "https://github.com/Neutree/COMTool/releases"));
        links.Children.Add(LinkButton(
            "Issues",
            "https://github.com/Neutree/COMTool/issues"));
        links.Children.Add(LinkButton("LGPL-3.0", "https://github.com/Neutree/COMTool#license"));
        links.Children.Add(LinkButton("Donate", "https://neucrack.com/donate"));

        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "COMTool",
                    FontSize = 26,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#009688")),
                },
                new TextBlock
                {
                    Text = "v0.01",
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#009688")),
                },
                new TextBlock
                {
                    Text = "A cross-platform serial port and SSH terminal.\n"
                        + "Open source, created by Neucrack and contributors.\n"
                        + "Welcome to improve it together and add plugins.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Config path:\n" + (_config?.ConfigPath ?? "(not loaded)"),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                },
                new TextBlock
                {
                    Text = "License: LGPL-3.0\n"
                        + "You can buy me half a cup of coffee if this software helps.",
                    TextWrapping = TextWrapping.Wrap,
                },
                links,
                ok,
            },
        };
        await dialog.ShowDialog(this);
    }

    private Button LinkButton(string text, string url)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
        };
        button.Click += async (_, _) =>
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(url));
            }
            catch
            {
                // No browser is available in some headless sessions.
            }
        };
        return button;
    }

    private void ApplyTheme(string skin)
    {
        if (_config is null)
        {
            return;
        }

        ThemeManager.Apply(skin);
        _config.Config.Skin = skin;
        _config.Save();
    }

    private void ApplyEncodingConfig()
    {
        if (_config is null)
        {
            return;
        }

        var selected = 1;
        for (var index = 0; index < EncodingCombo.Items.Count; index++)
        {
            if (EncodingCombo.Items[index] is ComboBoxItem item
                && string.Equals(
                    item.Content?.ToString(),
                    _config.Config.Encoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                selected = index;
                break;
            }
        }

        _applyingEncoding = true;
        try
        {
            EncodingCombo.SelectedIndex = selected;
        }
        finally
        {
            _applyingEncoding = false;
        }
    }

    private void OnEncodingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingEncoding
            || _config is null
            || sender is not ComboBox combo
            || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var encoding = item.Content?.ToString();
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return;
        }

        _config.Config.Encoding = encoding;
        _config.Save();
    }

    private void OnEncodingDropDownClosed(object? sender, EventArgs e)
    {
        // Let the ComboBox finish closing before moving focus back. Otherwise
        // the popup's focus restoration can immediately steal it again.
        RestoreTerminalFocus();
    }

    private void RestoreTerminalFocus()
    {
        Dispatcher.UIThread.Post(FocusSelectedTerminal, DispatcherPriority.Input);
    }

    private void FocusSelectedTerminal()
    {
        if (DataContext is MainViewModel { SelectedTab.Content: { } content })
        {
            content.Focus();
        }
    }

    private void OnSessionsToggle(object? sender, RoutedEventArgs e)
    {
        SetSidebarVisible(!_sidebarVisible);
    }

    private void OnSessionHandleClick(object? sender, RoutedEventArgs e)
    {
        if (_sidebarVisible)
        {
            // Ignore the click immediately following a hover-open.
            if (DateTime.UtcNow - _sessionShownAt < TimeSpan.FromMilliseconds(350))
            {
                return;
            }

            SetSidebarVisible(false);
            return;
        }

        SetSidebarVisible(true);
        _sessionShownAt = DateTime.UtcNow;
        _sessionHoverSeen = true;
    }

    private void OnSessionHandlePointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_sidebarVisible)
        {
            ShowSessionSidebarAuto();
        }
    }

    private void OnSessionHoverStripEntered(object? sender, PointerEventArgs e)
    {
        if (!_sidebarVisible)
        {
            ShowSessionSidebarAuto();
        }
    }

    private void OnSidebarPointerEntered(object? sender, PointerEventArgs e)
    {
        _sessionHoverSeen = true;
    }

    private void ShowSessionSidebarAuto()
    {
        if (_sidebarVisible)
        {
            return;
        }

        SetSidebarVisible(true, persist: false);
        _sessionShownAt = DateTime.UtcNow;
        _sessionHoverSeen = true;
    }

    private void SetSidebarVisible(bool visible, bool persist = true)
    {
        _sidebarVisible = visible;
        Sidebar.IsVisible = visible;
        SidebarSplitter.IsVisible = visible;
        BodyGrid.ColumnDefinitions[0].Width = visible
            ? new GridLength(ClampSidebarWidth(_sidebarWidth))
            : new GridLength(0);
        BodyGrid.ColumnDefinitions[1].Width = visible ? new GridLength(3) : new GridLength(0);
        SessionHandleIcon.Text = visible ? "\uf053" : "\uf054";
        SessionHoverStrip.IsVisible = !visible;

        if (visible)
        {
            _sessionHoverSeen = false;
            _lastSessionTimerX = _lastSessionPointerX;
            _sessionHoverTimer.Start();
        }
        else
        {
            _sessionHoverTimer.Stop();
            _sessionHoverSeen = false;
        }

        if (persist)
        {
            PersistSidebarConfig();
        }
    }

    private void CheckSessionSidebarAutoHide()
    {
        if (!_sidebarVisible)
        {
            _sessionHoverTimer.Stop();
            _sessionHoverSeen = false;
            return;
        }

        var dx = _lastSessionPointerX - _lastSessionTimerX;
        _lastSessionTimerX = _lastSessionPointerX;

        var inside = Sidebar.IsPointerOver
            || SessionHandle.IsPointerOver
            || SessionHoverStrip.IsPointerOver
            || IsInSessionSidebarApproach();
        if (inside)
        {
            _sessionHoverSeen = true;
            return;
        }

        // Moving right while outside the window's left edge approaches the panel.
        if (_lastSessionPointerX < 0)
        {
            return;
        }

        // Do not collapse while dragging the splitter, selecting text, or using a
        // context menu.
        if (_windowPointerPressed || Sidebar.HasOpenContextMenu)
        {
            return;
        }

        if (_sessionHoverSeen && dx > 0)
        {
            SetSidebarVisible(false, persist: false);
        }
    }

    private bool IsInSessionSidebarApproach()
    {
        var bodyOrigin = BodyGrid.TranslatePoint(new Point(0, 0), this) ?? new Point(16, 35);
        var bodyBottom = bodyOrigin.Y + BodyGrid.Bounds.Height;
        return _lastSessionPointerX >= 0
            && _lastSessionPointerX <= bodyOrigin.X
            && _lastSessionPointerY >= bodyOrigin.Y
            && _lastSessionPointerY <= bodyBottom;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        _lastSessionPointerX = position.X;
        _lastSessionPointerY = position.Y;

        if (e.Source is Control source
            && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            Cursor = null;
            return;
        }

        Cursor = GetResizeEdge(position) switch
        {
            WindowEdge.North or WindowEdge.South => NorthSouthCursor,
            WindowEdge.West or WindowEdge.East => WestEastCursor,
            WindowEdge.NorthWest or WindowEdge.SouthEast => NorthWestSouthEastCursor,
            WindowEdge.NorthEast or WindowEdge.SouthWest => NorthEastSouthWestCursor,
            _ => null,
        };
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _windowPointerPressed = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || WindowState != WindowState.Normal)
        {
            return;
        }

        // Window buttons at the very edge must still receive their click.
        if (e.Source is Control source
            && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        var edge = GetResizeEdge(e.GetPosition(this));
        if (edge is not null)
        {
            BeginResizeDrag(edge.Value, e);
            e.Handled = true;
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _windowPointerPressed = false;
    }

    private void OnWindowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _windowPointerPressed = false;
    }

    private WindowEdge? GetResizeEdge(Point point)
    {
        const double margin = 5;
        var left = point.X <= margin;
        var right = point.X >= Bounds.Width - margin;
        var top = point.Y <= margin;
        var bottom = point.Y >= Bounds.Height - margin;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null,
        };
    }

    private void ApplySidebarConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.SessionSidebar;
        _sidebarWidth = ClampSidebarWidth(config.Width);
        SetSidebarVisible(config.Visible, persist: false);
    }

    private void ApplyWindowSizeConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.Window;
        if (config is null)
        {
            return;
        }

        ApplyWindowSize(config);
    }

    private void ApplySavedWindowSizeBeforeShow()
    {
        if (Design.IsDesignMode || Environment.GetCommandLineArgs().Contains("--demo"))
        {
            return;
        }

        var config = AppConfigService.TryReadWindowConfig();
        if (config is not null)
        {
            ApplyWindowSize(config);
        }
    }

    private void ApplyWindowSize(WindowConfig config)
    {
        var width = NormalizeWindowSize(
            config.Width,
            DefaultWindowWidth,
            MinimumWindowWidth);
        var height = NormalizeWindowSize(
            config.Height,
            DefaultWindowHeight,
            MinimumWindowHeight);
        Width = width;
        Height = height;
        _lastNormalWidth = width;
        _lastNormalHeight = height;
    }

    private static double NormalizeWindowSize(int value, double fallback, double minimum) =>
        value >= minimum && value <= 10000 ? value : fallback;

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState != WindowState.Normal
            || !double.IsFinite(e.NewSize.Width)
            || !double.IsFinite(e.NewSize.Height)
            || e.NewSize.Width < MinimumWindowWidth
            || e.NewSize.Height < MinimumWindowHeight)
        {
            return;
        }

        _lastNormalWidth = e.NewSize.Width;
        _lastNormalHeight = e.NewSize.Height;
    }

    private void PersistWindowSize()
    {
        if (_config is null)
        {
            return;
        }

        try
        {
            var width = (int)Math.Round(
                Math.Clamp(_lastNormalWidth, MinimumWindowWidth, 10000));
            var height = (int)Math.Round(
                Math.Clamp(_lastNormalHeight, MinimumWindowHeight, 10000));
            var config = _config.Config.Window;
            if (config is null)
            {
                config = new WindowConfig();
                _config.Config.Window = config;
            }

            if (config.Width == width && config.Height == height)
            {
                return;
            }

            config.Width = width;
            config.Height = height;
            _config.Save();
            AppLog.Info($"Window size saved: {width}x{height}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save window size", ex);
        }
    }

    private void OnSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _sidebarWidth = ClampSidebarWidth(BodyGrid.ColumnDefinitions[0].ActualWidth);
        PersistSidebarConfig();
    }

    private static double ClampSidebarWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, 160, 320) : 220;

    private void PersistSidebarConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.SessionSidebar;
        config.Visible = _sidebarVisible;
        config.Width = (int)Math.Round(ClampSidebarWidth(_sidebarWidth));
        _config.Save();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeIcon();
        RestoreTerminalFocus();
    }

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\U000F05B2" : "\U000F05AF";
        ToolTip.SetTip(MaximizeButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private void OnAddTerminalClick(object? sender, RoutedEventArgs e)
    {
        ShowConnectionSettings(null);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedTab: { } tab })
        {
            return;
        }

        if (tab.Kind == SessionKind.Local)
        {
            await ShowErrorAsync("This development terminal has no saved connection settings.");
            RestoreTerminalFocus();
            return;
        }

        ShowConnectionSettings(tab);
    }

    private void ShowConnectionSettings(TerminalTab? tab)
    {
        if (_config is null)
        {
            return;
        }

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new ConnectionSettingsWindow(
            _config,
            tab,
            OpenSerialFromSettingsAsync,
            OpenSshFromSettingsAsync,
            ShowErrorAsync);
        _settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }

            RestoreTerminalFocus();
        };
        window.Show(this);
    }

    private async Task OpenSerialFromSettingsAsync(string port)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var existing = viewModel.FindTab(port, SessionKind.Serial);
            if (existing is not null)
            {
                CloseTab(existing);
            }
        }

        OpenSerialSession(port);
        RefreshSidebar();
        await Task.CompletedTask;
    }

    private async Task OpenSshFromSettingsAsync(string key)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var existing = viewModel.FindTab(key, SessionKind.Ssh);
            if (existing is not null)
            {
                CloseTab(existing);
            }
        }

        await OpenSshSessionAsync(key);
        RefreshSidebar();
    }

    private void OnFunctionalClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedTab: { } tab })
        {
            tab.IsFunctionalVisible = !tab.IsFunctionalVisible;
        }
    }

    private async void OnShareConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || DataContext is not MainViewModel { SelectedTab: { } tab })
        {
            return;
        }

        try
        {
            object? payload = null;
            var suggestedName = "comtool.session.json";
            if (tab.Kind == SessionKind.Serial)
            {
                payload = _config.SerialSessions.Get(tab.SessionId);
                suggestedName = "comtool.serial.json";
            }
            else if (tab.Kind == SessionKind.Ssh)
            {
                var session = _config.SshSessions.Get(tab.SessionId);
                if (session is not null)
                {
                    // Never export the DPAPI token. Copy the non-secret fields
                    // so sharing a config cannot clear the remembered password.
                    payload = new SshSession
                    {
                        Key = session.Key,
                        Host = session.Host,
                        Port = session.Port,
                        User = session.User,
                        Alias = session.Alias,
                        SshKey = session.SshKey,
                        Updated = session.Updated,
                    };
                }

                suggestedName = "comtool.ssh.json";
            }

            if (payload is null)
            {
                throw new InvalidOperationException("This tab has no shareable session config.");
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Share config",
                SuggestedFileName = suggestedName,
                DefaultExtension = "json",
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(
                stream,
                payload,
                payload.GetType(),
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void OnLoadConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load config",
                AllowMultiple = false,
            });
            if (files.Count == 0)
            {
                return;
            }

            await using var stream = await files[0].OpenReadAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            if (root.TryGetProperty("serial", out _))
            {
                var session = root.Deserialize<SerialSession>()
                    ?? throw new InvalidOperationException("Invalid serial session config.");
                if (session.Port.Trim().Length == 0)
                {
                    throw new InvalidOperationException("Serial config has no port.");
                }

                _config.SerialSessions.Upsert(session.Port, session.Serial, session.Name);
            }
            else if (root.TryGetProperty("host", out _))
            {
                var session = root.Deserialize<SshSession>()
                    ?? throw new InvalidOperationException("Invalid SSH session config.");
                _config.SshSessions.Upsert(
                    session.Host,
                    session.Port,
                    session.User,
                    alias: session.Alias,
                    sshKey: session.SshKey);
            }
            else
            {
                throw new InvalidOperationException("Unrecognized session config file.");
            }

            _config.Save();
            RefreshSidebar();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void OnUnsupportedPluginClick(object? sender, RoutedEventArgs e)
    {
        var name = sender is MenuItem item ? item.Header?.ToString() : "Plugin";
        await ShowErrorAsync($"{name} is not available in this build.");
    }

    private async void OnTerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TerminalControl terminal)
        {
            return;
        }

        var point = e.GetCurrentPoint(terminal);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (terminal.Terminal.Selection.HasSelection)
        {
            await terminal.CopyAsync();
            terminal.Terminal.Selection.ClearSelection();
            return;
        }

        await terminal.PasteAsync();
    }

    private async void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TerminalControl terminal || e.Key != Key.V)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var paste = modifiers == KeyModifiers.Control
            || modifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        if (!paste)
        {
            return;
        }

        e.Handled = true;
        await terminal.PasteAsync();
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        }

        return $"{bytesPerSecond:0} B/s";
    }

    private static string FormatEta(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private sealed record ZmodemTiming(string Name, DateTime StartedAt, long StartBytes);

    private sealed record DetachedTabState(Window Window, int Index)
    {
        public bool SuppressReattach { get; set; }
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistWindowSize();
        _portTimer?.Stop();
        _portTimer = null;
        SizeChanged -= OnWindowSizeChanged;
        Sidebar.SessionActivated -= OnSessionActivated;
        Sidebar.RemoveRequested -= OnSessionRemoveRequested;

        foreach (var state in _detachedTabs.Values.ToList())
        {
            state.SuppressReattach = true;
            state.Window.Close();
        }

        if (DataContext is MainViewModel viewModel)
        {
            foreach (var tab in viewModel.Tabs.ToList())
            {
                CloseTab(tab);
            }
        }

        base.OnClosed(e);
    }
}
