using System.Collections.ObjectModel;
using System.Collections.Specialized;
using COMTool.Core.Config;
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
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _appendEnter = true;
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
    /// True while at least one SSH session is connected. The MobaXterm rail
    /// shows its SSH-browser icon only while an SSH login exists.
    /// </summary>
    [ObservableProperty]
    private bool _isSshBrowserVisible;

    [ObservableProperty]
    private bool _isMacroEditorOpen;

    [ObservableProperty]
    private string _macroEditorName = "";

    [ObservableProperty]
    private string _macroEditorText = "";

    [ObservableProperty]
    private bool _macroEditorAppendEnter = true;

    [ObservableProperty]
    private string? _macroEditorError;

    [ObservableProperty]
    private string? _editingMacroId;

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

    public string SidebarTitle => SidebarTab switch
    {
        SidebarTab.Tools => "Tools",
        SidebarTab.Macros => "Macros",
        SidebarTab.SshBrowser => "SSH browser",
        _ => "User sessions",
    };

    public bool CanSendMacro => SelectedTab?.Terminal is not null;

    private void OnSidebarCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateSidebarEmptyStates();

    private void UpdateSidebarEmptyStates()
    {
        OnPropertyChanged(nameof(HasMacros));
    }

    public bool HasMacros => Macros.Count > 0;

    /// <summary>
    /// Opens the inline macro editor, optionally pre-filled for editing an
    /// existing macro.
    /// </summary>
    public void OpenMacroEditor(MacroItem? existing = null)
    {
        EditingMacroId = existing?.Id;
        MacroEditorName = existing?.Name ?? "";
        MacroEditorText = existing?.Text ?? "";
        MacroEditorAppendEnter = existing?.AppendEnter ?? true;
        MacroEditorError = null;
        IsMacroEditorOpen = true;
    }

    public void CloseMacroEditor()
    {
        IsMacroEditorOpen = false;
        EditingMacroId = null;
        MacroEditorError = null;
    }

    public MacroItem? FindMacro(string? id) =>
        id is null ? null : Macros.FirstOrDefault(m => m.Id == id);
}
