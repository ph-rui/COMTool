using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;

namespace COMTool.App.Views;

public partial class SessionSidebar : UserControl
{
    private ContextMenu? _contextMenu;

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

    public event EventHandler<SessionItem>? RemoveRequested;

    public bool HasOpenContextMenu => _contextMenu?.IsOpen == true;

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
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => SessionActivated?.Invoke(this, item);
        var remove = new MenuItem { Header = "Remove session" };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, item);
        menu.Items.Add(open);
        menu.Items.Add(remove);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu))
            {
                _contextMenu = null;
            }
        };
        _contextMenu = menu;
        menu.Open(control);
    }
}
