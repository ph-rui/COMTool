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

    public event EventHandler<MacroItem>? MacroSendRequested;

    public event EventHandler<MacroItem>? MacroEditRequested;

    public event EventHandler<MacroItem>? MacroDeleteRequested;

    public event EventHandler? MacroSaveRequested;

    public event EventHandler? MacroCancelRequested;

    public event EventHandler<HistoryEntry>? HistoryActivated;

    public event EventHandler? HistoryClearRequested;

    /// <summary>Raised with a <c>ToolCatalog</c> id when a tool menu row is clicked.</summary>
    public event EventHandler<string>? ToolRequested;

    public bool HasOpenNewSessionMenu => SidebarAddSessionButton.Flyout is { IsOpen: true };

    internal SidebarTab SelectedSidebarTabForTests =>
        DataContext is MainViewModel vm ? vm.SidebarTab : SidebarTab.Sessions;

    private void OnRailTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || DataContext is not MainViewModel viewModel
            || !Enum.TryParse<SidebarTab>(tag, out var tab))
        {
            return;
        }

        viewModel.SidebarTab = tab;
    }

    private void OnAddMacroClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.OpenMacroEditor();
        }
    }

    private void OnMacroSaveClick(object? sender, RoutedEventArgs e) =>
        MacroSaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnMacroCancelClick(object? sender, RoutedEventArgs e)
    {
        MacroCancelRequested?.Invoke(this, EventArgs.Empty);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CloseMacroEditor();
        }
    }

    private MacroItem? MacroOf(object? sender) =>
        sender is Control { DataContext: MacroItem item } ? item : null;

    private void OnMacroSendClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroSendRequested?.Invoke(this, item);
        }
    }

    private void OnMacroEditClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroEditRequested?.Invoke(this, item);
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.OpenMacroEditor(item);
            }
        }
    }

    private void OnMacroDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (MacroOf(sender) is { } item)
        {
            MacroDeleteRequested?.Invoke(this, item);
        }
    }

    private void OnHistoryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: HistoryEntry entry })
        {
            HistoryActivated?.Invoke(this, entry);
        }
    }

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e) =>
        HistoryClearRequested?.Invoke(this, EventArgs.Empty);

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
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => SessionActivated?.Invoke(this, item);
        var edit = new MenuItem { Header = "Edit session" };
        edit.Click += (_, _) => EditRequested?.Invoke(this, item);
        var remove = new MenuItem { Header = "Remove session" };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, item);
        menu.Items.Add(open);
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        return menu;
    }
}
