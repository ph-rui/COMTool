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
    private bool _sidebarVisible = true;
    private double _sidebarWidth = 220;
    private bool _loaded;
    private AppConfigService? _config;
    private DispatcherTimer? _portTimer;
    private ConnectionSettingsWindow? _settingsWindow;
    private static readonly IBrush StatusInfoBrush =
        new SolidColorBrush(Color.Parse("#008200"));
    private static readonly IBrush StatusWarningBrush =
        new SolidColorBrush(Color.Parse("#e65100"));
    private static readonly IBrush StatusErrorBrush =
        new SolidColorBrush(Color.Parse("#c62828"));
    private static readonly IBrush StatusProgressBrush =
        new SolidColorBrush(Color.Parse("#1565c0"));

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeIcon();
        Sidebar.SessionActivated += OnSessionActivated;
        Sidebar.RemoveRequested += OnSessionRemoveRequested;
        Opened += OnOpened;
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
                ApplySidebarConfig();
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

        var serial = viewModel.Tabs
            .Where(tab => tab.Kind == SessionKind.Serial && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ssh = viewModel.Tabs
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
            await ShowErrorAsync(ex.Message);
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
            IsTransportOpen = () => connection.IsOpen,
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

        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () => AttachTerminal(tab, terminal, connection.Pty),
                DispatcherPriority.Loaded);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            tab.IsAlive = false;
            tab.StatusMessage = "Connection lose";
            tab.StatusBrush = StatusWarningBrush;
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
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () => AttachTerminal(tab, terminal, connection.Pty),
                DispatcherPriority.Loaded);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            tab.IsAlive = false;
            tab.StatusMessage = "Connection lose";
            tab.StatusBrush = StatusWarningBrush;
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

    private TerminalControl CreateTerminal()
    {
        var terminal = new TerminalControl
        {
            FontFamily = new FontFamily("Consolas, Bitstream Vera Sans Mono, Courier New, monospace, Microsoft YaHei Mono, Microsoft YaHei"),
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

        zmodem.SendRequested += request =>
            Dispatcher.UIThread.Post(() => { _ = PromptZmodemSendAsync(tab, zmodem); });
        zmodem.Progress += progress => Dispatcher.UIThread.Post(() =>
        {
            var percent = progress.Total is > 0
                ? (int)(progress.Sent * 100 / progress.Total.Value)
                : 0;
            tab.StatusMessage =
                $"{(progress.IsSend ? "Sending" : "Receiving")} {progress.Name} {percent}%";
            tab.StatusBrush = StatusProgressBrush;
        });
        zmodem.TransferCompleted += result =>
            Dispatcher.UIThread.Post(() => { _ = CompleteZmodemAsync(tab, result); });
        zmodem.TransferFailed += message => Dispatcher.UIThread.Post(() =>
        {
            tab.StatusMessage = $"ZMODEM error: {message}";
            tab.StatusBrush = StatusErrorBrush;
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
        if (paths.Length == 0)
        {
            zmodem.CancelSend();
            tab.StatusMessage = "File transfer cancelled";
            tab.StatusBrush = StatusWarningBrush;
            return;
        }

        tab.StatusMessage = "Sending...";
        tab.StatusBrush = StatusProgressBrush;
        zmodem.StartSend(paths);
    }

    private async Task CompleteZmodemAsync(TerminalTab tab, ZmodemTransferResult result)
    {
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
        terminal.AttachedToVisualTree += (_, _) =>
        {
            if (!tab.IsAlive)
            {
                return;
            }

            try
            {
                terminal.LaunchProcess();
            }
            catch (Exception ex)
            {
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
        viewModel.CloseTab(tab);
        UpdateConnectionStates();
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
            Title = "COMTool",
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

    private void OnLightThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("light");

    private void OnDarkThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("dark");

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        await ShowInfoAsync(
            "About COMTool",
            "COMTool v3.4.1\n\n"
            + "A cross-platform serial port and SSH terminal.\n"
            + "https://github.com/Neutree/COMTool");
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

    private void OnSessionsToggle(object? sender, RoutedEventArgs e)
    {
        SetSidebarVisible(!_sidebarVisible);
    }

    private void OnSessionHandleClick(object? sender, RoutedEventArgs e)
    {
        SetSidebarVisible(!_sidebarVisible);
    }

    private void SetSidebarVisible(bool visible)
    {
        _sidebarVisible = visible;
        Sidebar.IsVisible = visible;
        SidebarSplitter.IsVisible = visible;
        BodyGrid.ColumnDefinitions[0].Width = visible
            ? new GridLength(ClampSidebarWidth(_sidebarWidth))
            : new GridLength(0);
        BodyGrid.ColumnDefinitions[1].Width = visible ? new GridLength(3) : new GridLength(0);
        SessionHandleIcon.Text = visible ? "\uf053" : "\uf054";
        PersistSidebarConfig();
    }

    private void ApplySidebarConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.SessionSidebar;
        _sidebarWidth = ClampSidebarWidth(config.Width);
        SetSidebarVisible(config.Visible);
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
    }

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uf2d2" : "\uf2d0";
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

    protected override void OnClosed(EventArgs e)
    {
        _portTimer?.Stop();
        _portTimer = null;
        Sidebar.SessionActivated -= OnSessionActivated;
        Sidebar.RemoveRequested -= OnSessionRemoveRequested;

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
