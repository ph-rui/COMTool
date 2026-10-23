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
    History
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

public sealed class HistoryEntry
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required long At { get; init; }
    public required SessionItem Session { get; init; }
    public string Name => Session.Name;
    public string Detail => Session.Detail ?? "";
    public string IconGlyph => Session.IconGlyph;
    public string TimeText { get; init; } = "";
}

public sealed class ScanResultItem
{
    public required string Address { get; init; }
    public string HostName { get; init; } = "";
    public string Ports { get; init; } = "";
    public string Latency { get; init; } = "";
    public bool HasHostName => HostName.Length > 0;
    public bool HasPorts => Ports.Length > 0;
    public bool HasLatency => Latency.Length > 0;
}

public partial class MainViewModel
{
    [ObservableProperty]
    private SidebarTab _sidebarTab = SidebarTab.Sessions;

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

    [ObservableProperty]
    private string _scanTarget = "";

    [ObservableProperty]
    private string _scanPorts = "22,23";

    [ObservableProperty]
    private string _scanStatus = "";

    [ObservableProperty]
    private string? _scanError;

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<MacroItem> Macros { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();
    public ObservableCollection<ScanResultItem> ScanResults { get; } = new();

    /// <summary>
    /// Wires up collection-changed notifications for the sidebar panels.
    /// Called once from the main constructor.
    /// </summary>
    public void InitializeSidebar()
    {
        Macros.CollectionChanged += OnSidebarCollectionChanged;
        History.CollectionChanged += OnSidebarCollectionChanged;
        ScanResults.CollectionChanged += OnSidebarCollectionChanged;
        UpdateSidebarEmptyStates();
    }

    partial void OnSidebarTabChanged(SidebarTab value)
    {
        OnPropertyChanged(nameof(IsSessionsTab));
        OnPropertyChanged(nameof(IsToolsTab));
        OnPropertyChanged(nameof(IsMacrosTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(SidebarTitle));
    }

    public bool IsSessionsTab => SidebarTab == SidebarTab.Sessions;
    public bool IsToolsTab => SidebarTab == SidebarTab.Tools;
    public bool IsMacrosTab => SidebarTab == SidebarTab.Macros;
    public bool IsHistoryTab => SidebarTab == SidebarTab.History;

    public string SidebarTitle => SidebarTab switch
    {
        SidebarTab.Tools => "Tools",
        SidebarTab.Macros => "Macros",
        SidebarTab.History => "History",
        _ => "User sessions",
    };

    public bool CanSendMacro => SelectedTab?.Terminal is not null;

    private void OnSidebarCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateSidebarEmptyStates();

    private void UpdateSidebarEmptyStates()
    {
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasScanResults));
    }

    public bool HasMacros => Macros.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool HasScanResults => ScanResults.Count > 0;

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

    public static string FormatRelativeTime(long atMillis, long nowMillis)
    {
        long seconds = Math.Max(0, (nowMillis - atMillis) / 1000);
        if (seconds < 60) return "just now";
        if (seconds < 3600) return $"{seconds / 60} min ago";
        if (seconds < 86400) return $"{seconds / 3600} h ago";
        return $"{seconds / 86400} d ago";
    }
}
