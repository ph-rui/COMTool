using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using COMTool.Core.Config;
using COMTool.Core.Macros;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

public enum SidebarTab
{
    Sessions,
    Tools,
    Macros,

    /// <summary>
    /// The SFTP browser of the selected SSH session. It takes the place
    /// of the sessions/tools/macros panel, the rail stays visible.
    /// </summary>
    SshBrowser,
}

public sealed partial class MacroItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string? _hotkey;

    public IReadOnlyList<MacroStep> Steps { get; private set; }

    /// <summary>Replaces the step list (editor 确定后回写).</summary>
    public void SetSteps(IEnumerable<MacroStep> steps) => Steps = steps.ToList();

    public MacroItem(string id, string name, IReadOnlyList<MacroStep> steps, string? hotkey = null)
    {
        Id = id;
        _name = name;
        _hotkey = hotkey;
        Steps = steps.ToList();
    }

    public string MenuRenameHeader => $"重命名 \"{Name}\"";
    public string MenuEditHeader => $"编辑 \"{Name}\"";
    public string MenuDuplicateHeader => $"复制 \"{Name}\"";
    public string MenuDeleteHeader => $"删除 \"{Name}\"";
    public string MenuHotkeyHeader => $"分配快捷键 \"{Name}\"";
    public string MenuExecuteHeader => $"执行 \"{Name}\"";

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(MenuRenameHeader));
        OnPropertyChanged(nameof(MenuEditHeader));
        OnPropertyChanged(nameof(MenuDuplicateHeader));
        OnPropertyChanged(nameof(MenuDeleteHeader));
        OnPropertyChanged(nameof(MenuHotkeyHeader));
        OnPropertyChanged(nameof(MenuExecuteHeader));
    }
}

/// <summary>
/// One entry of the MobaXterm-style tool menu. Entries come from
/// <c>ToolCatalog</c>; the Tools tab is a flat list of the standalone tool
/// windows.
/// </summary>
public sealed class ToolMenuItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsEnabled { get; init; }
    public string ToolTip { get; init; } = "";

    /// <summary>FontAwesome 6 solid glyph (e.g. "\uf0ac").</summary>
    public string Glyph { get; init; } = "";

    /// <summary>Icon color (hex), matching MobaXterm's colored menu icons.</summary>
    public string GlyphColor { get; init; } = "";
}

public partial class MainViewModel
{
    [ObservableProperty]
    private SidebarTab _sidebarTab = SidebarTab.Sessions;

    /// <summary>
    /// True while the selected tab is a live SSH session. The MobaXterm rail
    /// shows its SSH-browser icon only for the current SSH login, so a COM tab
    /// hides it even when other SSH tabs stay connected.
    /// </summary>
    [ObservableProperty]
    private bool _isSshBrowserVisible;

    public ObservableCollection<MacroItem> Macros { get; } = new();
    public ObservableCollection<ToolMenuItem> ToolMenu { get; } = new();

    /// <summary>
    /// Wires up collection-changed notifications for the sidebar panels.
    /// Called once from the main constructor.
    /// </summary>
    public void InitializeSidebar()
    {
        Macros.CollectionChanged += OnSidebarCollectionChanged;
        InitToolMenu();
        UpdateSidebarEmptyStates();
    }

    private void InitToolMenu()
    {
        // The Tools tab only lists the standalone tool windows from
        // ToolCatalog; adding a tool there is enough to make it appear.
        foreach (var tool in COMTool.App.Tools.ToolCatalog.All)
        {
            ToolMenu.Add(new ToolMenuItem
            {
                Id = tool.Id,
                Name = tool.Name,
                IsEnabled = tool.IsEnabled,
                ToolTip = tool.Name,
                Glyph = tool.Glyph,
                GlyphColor = tool.GlyphColor,
            });
        }
    }

    partial void OnSidebarTabChanged(SidebarTab value)
    {
        OnPropertyChanged(nameof(IsSessionsTab));
        OnPropertyChanged(nameof(IsToolsTab));
        OnPropertyChanged(nameof(IsMacrosTab));
        OnPropertyChanged(nameof(IsSshBrowserTab));
        OnPropertyChanged(nameof(RailSessionsActive));
        OnPropertyChanged(nameof(RailToolsActive));
        OnPropertyChanged(nameof(RailMacrosActive));
        OnPropertyChanged(nameof(RailSshBrowserActive));
        OnPropertyChanged(nameof(SidebarTitle));

        if (value == SidebarTab.SshBrowser)
        {
            if (SshBrowserPane is not { } pane)
            {
                // The browser belongs to an SSH session; an empty rail
                // cannot keep it open.
                SidebarTab = SidebarTab.Sessions;
                return;
            }

            pane.IsExpanded = true;
            _ = pane.EnsureLoadedAsync(CancellationToken.None);
        }
        else
        {
            SshBrowserPane?.IsExpanded = false;
        }
    }

    /// <summary>
    /// The SFTP browser that the sidebar shows. It is the panel of the
    /// selected tab, which is an SSH tab whenever the browser is open.
    /// </summary>
    public SftpPaneViewModel? SshBrowserPane => SelectedTab?.SftpPane;

    public bool IsSessionsTab => SidebarTab == SidebarTab.Sessions;
    public bool IsToolsTab => SidebarTab == SidebarTab.Tools;
    public bool IsMacrosTab => SidebarTab == SidebarTab.Macros;
    public bool IsSshBrowserTab => SidebarTab == SidebarTab.SshBrowser;

    /// <summary>
    /// Rail icon highlight state: only while the panel is actually open on
    /// the matching tab. A collapsed panel shows no active rail icon
    /// (user feedback 2026-10-29: collapsed star looked stuck pressed).
    /// </summary>
    public bool RailSessionsActive => IsSidebarVisible && IsSessionsTab;
    public bool RailToolsActive => IsSidebarVisible && IsToolsTab;
    public bool RailMacrosActive => IsSidebarVisible && IsMacrosTab;
    public bool RailSshBrowserActive => IsSidebarVisible && IsSshBrowserTab;

    public string SidebarTitle => SidebarTab switch
    {
        SidebarTab.Tools => "Tools",
        SidebarTab.Macros => "宏",
        SidebarTab.SshBrowser => "SSH browser",
        _ => "User sessions",
    };

    private void OnSidebarCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateSidebarEmptyStates();

    private void UpdateSidebarEmptyStates()
    {
        OnPropertyChanged(nameof(HasMacros));
    }

    public bool HasMacros => Macros.Count > 0;

    public MacroItem? FindMacro(string? id) =>
        id is null ? null : Macros.FirstOrDefault(m => m.Id == id);
}
