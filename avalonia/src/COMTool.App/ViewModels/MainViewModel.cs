using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

public sealed class SessionItem
{
    public required string Name { get; init; }
    public string? Detail { get; init; }
    public bool IsConnected { get; init; }
    public bool IsSsh { get; init; }

    public string IconGlyph => IsSsh ? "\uf233" : "\uf1e6";
    public IBrush IconBrush => IsConnected
        ? new SolidColorBrush(Color.Parse("#43a047"))
        : new SolidColorBrush(Color.Parse("#90a4ae"));
}

public sealed class SidebarSection
{
    public required string Title { get; init; }
    public bool ShowHeader { get; init; }
    public ObservableCollection<SessionItem> Items { get; } = new();
}

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<SidebarSection> Sections { get; } = new();
    public bool HasSessions { get; private set; }
    public bool IsSidebarVisible { get; set; } = true;

    public MainViewModel()
    {
        var demo = Environment.GetCommandLineArgs().Contains("--demo");
        if (!demo)
        {
            return;
        }

        var serial = new SidebarSection { Title = "Serial", ShowHeader = true };
        serial.Items.Add(new SessionItem { Name = "COM99", Detail = "USB-SERIAL CH340", IsConnected = true });
        serial.Items.Add(new SessionItem { Name = "COM3", Detail = "Silicon Labs CP210x" });

        var ssh = new SidebarSection { Title = "SSH", ShowHeader = true };
        ssh.Items.Add(new SessionItem
        {
            Name = "lab",
            Detail = "root@192.168.1.10:22",
            IsConnected = true,
            IsSsh = true,
        });

        Sections.Add(serial);
        Sections.Add(ssh);
        HasSessions = true;
    }
}
