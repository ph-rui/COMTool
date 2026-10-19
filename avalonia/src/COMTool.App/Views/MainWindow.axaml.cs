using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

public partial class MainWindow : Window
{
    private bool _sidebarVisible = true;
    private TerminalControl? _terminal;

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeIcon();
        Opened += (_, _) =>
        {
            // Phase 0 smoke-test hook; removed when the real plugin host lands.
            if (Environment.GetCommandLineArgs().Contains("--terminal"))
            {
                OnAddTerminalClick(null, new RoutedEventArgs());
            }
        };
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
        BodyGrid.ColumnDefinitions[0].Width = visible ? new GridLength(220) : new GridLength(0);
        BodyGrid.ColumnDefinitions[1].Width = visible ? new GridLength(3) : new GridLength(0);
        SessionHandleIcon.Text = visible ? "\uf053" : "\uf054";
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
        EmptyState.IsVisible = false;
        TabHost.IsVisible = true;
        ActiveTabTitle.Text = "terminal";

        if (_terminal is not null)
        {
            return;
        }

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
        terminal.PointerPressed += OnTerminalPointerPressed;
        terminal.KeyDown += OnTerminalKeyDown;
        terminal.AttachedToVisualTree += (_, _) =>
        {
            try
            {
                terminal.LaunchProcess();
            }
            catch (Exception ex)
            {
                TabContentHost.Content = new TextBlock
                {
                    Text = $"Failed to start terminal: {ex.Message}",
                    Margin = new Thickness(12),
                    Foreground = new SolidColorBrush(Color.Parse("#f44336")),
                    TextWrapping = TextWrapping.Wrap,
                };
            }
        };
        _terminal = terminal;
        TabContentHost.Content = terminal;
        terminal.Focus();
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

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        _terminal?.Kill();
        _terminal = null;
        TabContentHost.Content = null;
        TabHost.IsVisible = false;
        EmptyState.IsVisible = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _terminal?.Kill();
        base.OnClosed(e);
    }
}
