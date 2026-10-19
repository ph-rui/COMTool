using Avalonia.Controls;
using Avalonia.Input;
using COMTool.App.ViewModels;

namespace COMTool.App.Views;

public partial class SessionSidebar : UserControl
{
    public SessionSidebar()
    {
        InitializeComponent();
    }

    public event EventHandler<SessionItem>? SessionActivated;

    private void OnSessionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SessionItem item })
        {
            SessionActivated?.Invoke(this, item);
        }
    }
}
