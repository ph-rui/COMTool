namespace COMTool.Core.Macros;

/// <summary>
/// Migrates legacy macro storage (single command text + optional trailing
/// ENTER) to the step-based model, preserving the exact bytes that were
/// sent before the upgrade.
/// </summary>
public static class MacroMigrator
{
    public static IReadOnlyList<MacroStep> FromLegacy(string? text, bool appendEnter)
    {
        var steps = new List<MacroStep>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Keep the text as a single step so playback sends the exact
            // same byte sequence (including any embedded newlines).
            steps.Add(MacroStep.TextStep(text));
        }

        if (appendEnter)
        {
            steps.Add(MacroStep.KeyStep(MacroKeys.Return));
        }

        return steps;
    }
}
