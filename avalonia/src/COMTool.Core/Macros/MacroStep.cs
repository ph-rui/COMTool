namespace COMTool.Core.Macros;

/// <summary>
/// The four step types supported by the macro system, matching MobaXterm 22.1.
/// </summary>
public enum MacroStepKind
{
    Text,
    Key,
    Sleep,
    WaitString,
}

/// <summary>
/// A single step within a macro. Uses a flat structure with a Kind discriminator;
/// only the fields relevant to the Kind are populated.
/// </summary>
public sealed class MacroStep
{
    public MacroStepKind Kind { get; }
    public string Text { get; }
    public int KeyDelayMs { get; }
    public string KeyName { get; }
    public int Milliseconds { get; }
    public string Pattern { get; }
    public int TimeoutMs { get; }

    private MacroStep(
        MacroStepKind kind,
        string text = "",
        int keyDelayMs = 0,
        string keyName = "",
        int milliseconds = 0,
        string pattern = "",
        int timeoutMs = 30_000)
    {
        Kind = kind;
        Text = text;
        KeyDelayMs = keyDelayMs;
        KeyName = keyName;
        Milliseconds = milliseconds;
        Pattern = pattern;
        TimeoutMs = timeoutMs;
    }

    public static MacroStep TextStep(string text, int keyDelayMs = 0) =>
        new(MacroStepKind.Text, text: text, keyDelayMs: keyDelayMs);

    public static MacroStep KeyStep(string keyName) =>
        new(MacroStepKind.Key, keyName: keyName);

    public static MacroStep SleepStep(int ms) =>
        new(MacroStepKind.Sleep, milliseconds: ms);

    public static MacroStep WaitStep(string pattern, int timeoutMs = 30_000) =>
        new(MacroStepKind.WaitString, pattern: pattern, timeoutMs: timeoutMs);

    /// <summary>
    /// Display text for the step editor list, matching MobaXterm's format:
    /// "text" / RETURN / SLEEP ms=1200 / WAIT "pattern"
    /// </summary>
    public string DisplaySummary => Kind switch
    {
        MacroStepKind.Text => Text.Contains('\n')
            ? $"\"{Text.Split('\n')[0]}…\""
            : $"\"{Text}\"",
        MacroStepKind.Key => KeyName,
        MacroStepKind.Sleep => $"SLEEP ms={Milliseconds}",
        MacroStepKind.WaitString => $"WAIT \"{Pattern}\"",
        _ => "?",
    };

    public MacroStepDto ToDto() => Kind switch
    {
        MacroStepKind.Text => new MacroStepDto
        {
            Kind = "text",
            Text = Text,
            KeyDelayMs = KeyDelayMs,
        },
        MacroStepKind.Key => new MacroStepDto
        {
            Kind = "key",
            Key = KeyName,
        },
        MacroStepKind.Sleep => new MacroStepDto
        {
            Kind = "sleep",
            Milliseconds = Milliseconds,
        },
        MacroStepKind.WaitString => new MacroStepDto
        {
            Kind = "wait",
            Pattern = Pattern,
            TimeoutMs = TimeoutMs,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public static MacroStep FromDto(MacroStepDto dto) => dto.Kind switch
    {
        "text" => TextStep(dto.Text ?? "", dto.KeyDelayMs),
        "key" => KeyStep(dto.Key ?? "RETURN"),
        "sleep" => SleepStep(dto.Milliseconds),
        "wait" => WaitStep(dto.Pattern ?? "", dto.TimeoutMs > 0 ? dto.TimeoutMs : 30_000),
        _ => SleepStep(0), // unknown → no-op
    };
}

/// <summary>
/// Supported key names for Key steps, with their Windows VK codes and
/// xterm escape sequences for terminal playback.
/// </summary>
public static class MacroKeys
{
    public const string Return = "RETURN";

    public static readonly string[] All =
    {
        "RETURN", "TAB", "BACKSPACE", "ESC",
        "UP", "DOWN", "LEFT", "RIGHT",
        "HOME", "END", "PAGEUP", "PAGEDOWN",
        "INSERT", "DELETE",
        "F1", "F2", "F3", "F4", "F5", "F6",
        "F7", "F8", "F9", "F10", "F11", "F12",
    };

    private static readonly Dictionary<string, (int Vk, string Seq)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RETURN"] = (13, "\r"),
        ["TAB"] = (9, "\t"),
        ["BACKSPACE"] = (8, "\x7f"),
        ["ESC"] = (27, "\x1b"),
        ["UP"] = (38, "\x1b[A"),
        ["DOWN"] = (40, "\x1b[B"),
        ["RIGHT"] = (39, "\x1b[C"),
        ["LEFT"] = (37, "\x1b[D"),
        ["HOME"] = (36, "\x1b[H"),
        ["END"] = (35, "\x1b[F"),
        ["PAGEUP"] = (33, "\x1b[5~"),
        ["PAGEDOWN"] = (34, "\x1b[6~"),
        ["INSERT"] = (45, "\x1b[2~"),
        ["DELETE"] = (46, "\x1b[3~"),
        ["F1"] = (112, "\x1bOP"),
        ["F2"] = (113, "\x1bOQ"),
        ["F3"] = (114, "\x1bOR"),
        ["F4"] = (115, "\x1bOS"),
        ["F5"] = (116, "\x1b[15~"),
        ["F6"] = (117, "\x1b[17~"),
        ["F7"] = (118, "\x1b[18~"),
        ["F8"] = (119, "\x1b[19~"),
        ["F9"] = (120, "\x1b[20~"),
        ["F10"] = (121, "\x1b[21~"),
        ["F11"] = (122, "\x1b[23~"),
        ["F12"] = (123, "\x1b[24~"),
    };

    public static bool TryGetSequence(string keyName, out string sequence)
    {
        if (Map.TryGetValue(keyName, out var entry))
        {
            sequence = entry.Seq;
            return true;
        }
        sequence = "";
        return false;
    }

    public static bool TryGetVkCode(string keyName, out int vk)
    {
        if (Map.TryGetValue(keyName, out var entry))
        {
            vk = entry.Vk;
            return true;
        }
        vk = 0;
        return false;
    }
}
