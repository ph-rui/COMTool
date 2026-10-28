namespace COMTool.Core.Macros;

/// <summary>
/// A macro: a named, ordered list of input steps (MobaXterm "宏").
/// Mirrors the MobaXterm 22.1 model: name + optional hotkey + steps.
/// </summary>
public sealed class MacroDefinition
{
    public string Name { get; set; } = "";
    public string? Hotkey { get; set; }
    public List<MacroStep> Steps { get; set; } = new();

    public MacroDefinition() { }

    public MacroDefinition(string name, IEnumerable<MacroStep> steps, string? hotkey = null)
    {
        Name = name;
        Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey;
        Steps = new List<MacroStep>(steps);
    }

    public bool HasSteps => Steps.Count > 0;
}
