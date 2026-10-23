using Avalonia.Controls;
using COMTool.App.Views;

namespace COMTool.App.Tools;

/// <summary>
/// One entry of the 工具 tab. Adding a tool means adding a descriptor to
/// <see cref="ToolCatalog"/> plus its window class; the menu, the sidebar
/// wiring and MainWindow's single-instance logic need no changes.
/// </summary>
/// <param name="Id">Stable identifier used by the menu and window registry.</param>
/// <param name="Name">Chinese menu label (MobaXterm wording).</param>
/// <param name="Glyph">FontAwesome 6 solid glyph, e.g. "\uf013".</param>
/// <param name="GlyphColor">Icon color as a hex string.</param>
/// <param name="IsEnabled">False keeps the row visible but disabled.</param>
/// <param name="CreateWindow">Creates the standalone window for the owner.</param>
public sealed record ToolDescriptor(
    string Id,
    string Name,
    string Glyph,
    string GlyphColor,
    bool IsEnabled,
    Func<MainWindow, Window> CreateWindow);

/// <summary>
/// The complete 工具 menu, 1:1 with the three standalone windows MobaXterm
/// 22.1 exposes on this target: 网络服务器, 网络扫描仪, 端口扫描仪.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> All { get; } = new[]
    {
        new ToolDescriptor(
            "net-server",
            "网络服务器",
            "\uf013",
            "#00897b",
            true,
            owner => new ServersManagementWindow(owner.ConfigService)),
        new ToolDescriptor(
            "scanner",
            "网络扫描仪",
            "\uf7c0",
            "#5e35b1",
            true,
            _ => new NetworkScanWindow()),
        new ToolDescriptor(
            "port-scanner",
            "端口扫描仪",
            "\uf1eb",
            "#2196f3",
            true,
            _ => new PortScanWindow()),
    };

    public static ToolDescriptor? Find(string id) =>
        All.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.Ordinal));
}
