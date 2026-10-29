using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;

namespace COMTool.App.Views;

public partial class SessionSidebar : UserControl
{
    public SessionSidebar()
    {
        InitializeComponent();
        AddHandler(
            PointerReleasedEvent,
            OnSidebarPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public event EventHandler<SessionItem>? SessionActivated;

    public event EventHandler<SessionItem>? EditRequested;

    public event EventHandler<SessionItem>? RemoveRequested;

    public event EventHandler<string>? NewSessionRequested;

    public event EventHandler? MacroRecordToggleRequested;

    public event EventHandler<MacroItem>? MacroExecuteRequested;

    public event EventHandler<MacroItem>? MacroRenameRequested;

    public event EventHandler<MacroItem>? MacroEditRequested;

    public event EventHandler<MacroItem>? MacroDuplicateRequested;

    public event EventHandler<MacroItem>? MacroDeleteRequested;

    public event EventHandler<MacroItem>? MacroHotkeyRequested;

    public event EventHandler? MacroImportRequested;

    public event EventHandler? MacroExportRequested;

    /// <summary>
    /// Raised when the Sessions, Tools or Macros rail icon is clicked. The
    /// main window decides whether to switch the panel or to collapse it:
    /// clicking the icon of the panel that is already open closes the panel
    /// while the rail stays on screen.
    /// </summary>
    public event EventHandler<SidebarTab>? RailTabRequested;

    /// <summary>
    /// Raised when the SSH-browser rail icon is clicked. The main window
    /// shows the SFTP browser of the current SSH session, or collapses the
    /// panel when the browser is already the open tab.
    /// </summary>
    public event EventHandler? SshBrowserRequested;

    /// <summary>Raised with a <c>ToolCatalog</c> id when a tool menu row is clicked.</summary>
    public event EventHandler<string>? ToolRequested;

    public bool HasOpenNewSessionMenu => SidebarAddSessionButton.Flyout is { IsOpen: true };

    internal SidebarTab SelectedSidebarTabForTests =>
        DataContext is MainViewModel vm ? vm.SidebarTab : SidebarTab.Sessions;

    internal void RaiseMacroRecordToggleForTests() =>
        MacroRecordToggleRequested?.Invoke(this, EventArgs.Empty);

    internal void RaiseMacroDeleteForTests(MacroItem item) =>
        MacroDeleteRequested?.Invoke(this, item);

    internal void RaiseMacroImportForTests() =>
        MacroImportRequested?.Invoke(this, EventArgs.Empty);

    internal void RaiseMacroExportForTests() =>
        MacroExportRequested?.Invoke(this, EventArgs.Empty);

    internal void RaiseMacroEditForTests(MacroItem item) =>
        MacroEditRequested?.Invoke(this, item);

    private void OnRailTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || DataContext is not MainViewModel
            || !Enum.TryParse<SidebarTab>(tag, out var tab))
        {
            return;
        }

        // The main window owns the open/collapsed state of the dock, so it
        // receives the request instead of the tab being switched here.
        RailTabRequested?.Invoke(this, tab);
    }

    private void OnMacroRecordToggleClick(object? sender, RoutedEventArgs e) =>
        MacroRecordToggleRequested?.Invoke(this, EventArgs.Empty);

    private MacroItem? MacroOf(object? sender) =>
        sender is Control { DataContext: MacroItem item } ? item : null;

    /// <summary>
    /// 单击执行（MobaXterm 行为，RDP 实机核对 2026-10-28）；右键出上下文菜单。
    /// </summary>
    private void OnMacroRowTapped(object? sender, TappedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroExecuteRequested?.Invoke(this, item);
        }
    }

    private void OnMacroRenameClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroRenameRequested?.Invoke(this, item);
        }
    }

    private void OnMacroEditClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroEditRequested?.Invoke(this, item);
        }
    }

    private void OnMacroDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroDuplicateRequested?.Invoke(this, item);
        }
    }

    private void OnMacroDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroDeleteRequested?.Invoke(this, item);
        }
    }

    private void OnMacroHotkeyClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroHotkeyRequested?.Invoke(this, item);
        }
    }

    private void OnMacroExecuteClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroExecuteRequested?.Invoke(this, item);
        }
    }

    private void OnMacroImportClick(object? sender, RoutedEventArgs e) =>
        MacroImportRequested?.Invoke(this, EventArgs.Empty);

    private void OnMacroExportClick(object? sender, RoutedEventArgs e) =>
        MacroExportRequested?.Invoke(this, EventArgs.Empty);

    private void OnRailSshBrowserClick(object? sender, RoutedEventArgs e) =>
        SshBrowserRequested?.Invoke(this, EventArgs.Empty);

    private void OnToolMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            ToolRequested?.Invoke(this, id);
        }
    }

    private void OnNewSessionMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            NewSessionRequested?.Invoke(this, tag);
        }
    }

    private void OnSessionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SessionItem item })
        {
            SessionActivated?.Invoke(this, item);
        }
    }

    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right
            || e.Source is not Control source)
        {
            return;
        }

        Control? control = source;
        while (control is not null && control.DataContext is not SessionItem)
        {
            control = control.GetVisualParent() as Control;
        }

        if (control?.DataContext is not SessionItem item)
        {
            return;
        }

        e.Handled = true;
        CreateSessionContextMenu(item).Open(control);
    }

    internal ContextMenu CreateSessionContextMenu(SessionItem item)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开" };
        open.Click += (_, _) => SessionActivated?.Invoke(this, item);
        var edit = new MenuItem { Header = "编辑会话" };
        edit.Click += (_, _) => EditRequested?.Invoke(this, item);
        var remove = new MenuItem { Header = "移除会话" };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, item);
        menu.Items.Add(open);
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        return menu;
    }
}
