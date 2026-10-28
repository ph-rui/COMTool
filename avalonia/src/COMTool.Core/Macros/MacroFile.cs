using System.Text;

namespace COMTool.Core.Macros;

/// <summary>
/// Result of parsing a .mxtmacros file: the imported macros plus
/// human-readable warnings for entries that had to be skipped.
/// </summary>
public sealed record MacroFileParseResult(IReadOnlyList<MacroDefinition> Macros, IReadOnlyList<string> Warnings);

/// <summary>
/// Reader/writer for the MobaXterm macro file format (.mxtmacros).
///
/// Format (verified against a real MobaXterm 22.1 export, 2026-10-28):
///
///   [Macros]
///   Name1=12:0:0:echo hello|258:13:1835009:RETURN|0:0:0:SLEEPEQUAL1200|
///   Name2=...
///
///   [MacrosHotkeys]
///   Name1=0
///
/// Each step is "typeId:field1:field2:payload" separated by '|', with a
/// trailing '|' after the last step. Known type ids:
///   0  = sleep, payload "SLEEPEQUAL&lt;ms&gt;" (plain "SLEEP&lt;ms&gt;" also accepted)
///   12 = text, field1 = per-key delay in ms, payload = the text (may contain ':')
///   258 = key, field1 = Windows virtual-key code, payload = key name (may contain ':')
///   4  = wait-for-string, field2 = timeout ms, payload = "WAIT:&lt;pattern&gt;"
///        (COMTool extension: the real MobaXterm wait encoding was never
///        observed; this layout is ours so COMTool round-trips losslessly.
///        Unknown type ids on import are skipped with a warning.)
/// </summary>
public static class MacroFile
{
    public const string DefaultFileName = "MobaXterm Macros.mxtmacros";

    public const string FileFilter = "Macros file (*.mxtmacros)|*.mxtmacros";

    public const string FileFilterLabel = "Macros file";

    public const string FileFilterPattern = "*.mxtmacros";

    public const int TypeSleep = 0;
    public const int TypeText = 12;
    public const int TypeKey = 258;
    public const int TypeWait = 4;

    public static string Export(IReadOnlyList<MacroDefinition> macros)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Macros]");
        foreach (var macro in macros)
        {
            sb.Append(EscapeName(macro.Name)).Append('=');
            foreach (var step in macro.Steps)
            {
                sb.Append(EncodeStep(step)).Append('|');
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("[MacrosHotkeys]");
        foreach (var macro in macros)
        {
            // MobaXterm's hotkey encoding is undocumented; 0 = no hotkey.
            sb.Append(EscapeName(macro.Name)).Append("=0").AppendLine();
        }

        return sb.ToString();
    }

    public static MacroFileParseResult Parse(string content)
    {
        var macros = new List<MacroDefinition>();
        var warnings = new List<string>();

        var (macroEntries, hotkeys) = SplitSections(content, warnings);
        foreach (var (name, stepsRaw) in macroEntries)
        {
            var steps = new List<MacroStep>();
            foreach (var rawStep in SplitSteps(stepsRaw, name, warnings))
            {
                if (TryDecodeStep(rawStep, name, warnings, out var step) && step is not null)
                {
                    steps.Add(step);
                }
            }

            if (steps.Count == 0)
            {
                warnings.Add($"Macro \"{name}\": no recognizable steps, skipped.");
                continue;
            }

            string? hotkey = null;
            if (hotkeys.TryGetValue(name, out var hk) && hk != 0)
            {
                // MobaXterm's hotkey code encoding is undocumented; keep the
                // macro but drop the hotkey rather than guessing.
                warnings.Add($"Macro \"{name}\": unknown hotkey code {hk}, ignored.");
            }

            macros.Add(new MacroDefinition(name, steps, hotkey));
        }

        return new MacroFileParseResult(macros, warnings);
    }

    public static string EncodeStep(MacroStep step) => step.Kind switch
    {
        MacroStepKind.Text => $"{TypeText}:{step.KeyDelayMs}:0:{step.Text}",
        MacroStepKind.Key => TryGetKeyCodes(step.KeyName, out var vk, out _)
            ? $"{TypeKey}:{vk}:0:{step.KeyName}"
            : $"{TypeKey}:0:0:{step.KeyName}",
        MacroStepKind.Sleep => $"{TypeSleep}:0:0:SLEEPEQUAL{step.Milliseconds}",
        MacroStepKind.WaitString => $"{TypeWait}:0:{step.TimeoutMs}:WAIT:{step.Pattern}",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    public static bool TryGetKeyCodes(string keyName, out int vk, out string sequence)
    {
        if (MacroKeys.TryGetVkCode(keyName, out vk)
            && MacroKeys.TryGetSequence(keyName, out sequence))
        {
            return true;
        }

        vk = 0;
        sequence = "";
        return false;
    }

    private static (Dictionary<string, string> MacroEntries, Dictionary<string, int> Hotkeys) SplitSections(string content, List<string> warnings)
    {
        var entries = new Dictionary<string, string>();
        var hotkeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var section = "";
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (line == "[Macros]") { section = "macros"; continue; }
            if (line == "[MacrosHotkeys]") { section = "hotkeys"; continue; }
            if (line.StartsWith('[')) { section = ""; continue; } // unknown section

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            switch (section)
            {
                case "macros":
                    if (key.Contains('\n'))
                    {
                        warnings.Add($"Skipping macro with invalid name at line {i + 1}.");
                        continue;
                    }

                    if (entries.ContainsKey(key))
                    {
                        warnings.Add($"Duplicate macro \"{key}\" in file; keeping the first one.");
                        continue;
                    }

                    entries[key] = value;
                    break;

                case "hotkeys":
                    if (int.TryParse(value, out var code))
                    {
                        hotkeys[key] = code;
                    }
                    break;
            }
        }

        return (entries, hotkeys);
    }

    private static IEnumerable<string> SplitSteps(string value, string macroName, List<string> warnings)
    {
        // Trailing '|' is part of the format; drop empty segments.
        foreach (var raw in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = raw.Trim();
            if (step.Length > 0) yield return step;
        }
    }

    private static bool TryDecodeStep(string raw, string macroName, List<string> warnings, out MacroStep? step)
    {
        // Split on the first three ':' so the payload may contain ':' itself.
        var idx1 = raw.IndexOf(':');
        var idx2 = raw.IndexOf(':', idx1 < 0 ? 0 : idx1 + 1);
        var idx3 = idx2 < 0 ? -1 : raw.IndexOf(':', idx2 + 1);

        if (idx1 < 0 || idx2 < 0)
        {
            warnings.Add($"Macro \"{macroName}\": skipping malformed step \"{Truncate(raw)}\".");
            step = null;
            return false;
        }

        string typeIdPart = idx1 < 0 ? raw : raw[..idx1];
        string f1 = idx1 < 0 ? "" : raw[(idx1 + 1)..(idx2 < 0 ? raw.Length : idx2)];
        string f2 = idx2 < 0 ? "" : raw[(idx2 + 1)..(idx3 < 0 ? raw.Length : idx3)];
        string payload = idx3 < 0 ? "" : raw[(idx3 + 1)..];

        if (!int.TryParse(typeIdPart, out var typeId))
        {
            warnings.Add($"Macro \"{macroName}\": skipping unknown step type \"{Truncate(raw)}\".");
            step = null;
            return false;
        }

        switch (typeId)
        {
            case TypeText:
            {
                int.TryParse(f1, out var keyDelay);
                step = MacroStep.TextStep(payload, Math.Max(0, keyDelay));
                return true;
            }
            case TypeKey:
                step = MacroStep.KeyStep(payload.Length > 0 ? payload : "RETURN");
                return true;
            case TypeSleep:
            {
                var text = payload;
                if (text.StartsWith("SLEEPEQUAL", StringComparison.Ordinal)) text = text["SLEEPEQUAL".Length..];
                else if (text.StartsWith("SLEEP", StringComparison.Ordinal)) text = text["SLEEP".Length..];
                if (int.TryParse(text.Trim(), out var ms))
                {
                    step = MacroStep.SleepStep(Math.Max(0, ms));
                    return true;
                }

                warnings.Add($"Macro \"{macroName}\": skipping malformed sleep step \"{Truncate(raw)}\".");
                step = null;
                return false;
            }
            case TypeWait:
            {
                var pattern = payload;
                if (pattern.StartsWith("WAIT:", StringComparison.Ordinal)) pattern = pattern["WAIT:".Length..];
                int.TryParse(f2, out var waitTimeout);
                step = MacroStep.WaitStep(pattern, waitTimeout > 0 ? waitTimeout : 30_000);
                return true;
            }
            default:
                warnings.Add($"Macro \"{macroName}\": skipping unknown step type {typeId} (\"{Truncate(raw)}\").");
                step = null;
                return false;
        }
    }

    private static string EscapeName(string name) => name.Replace('\n', ' ').Replace('\r', ' ');

    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "…";
}
