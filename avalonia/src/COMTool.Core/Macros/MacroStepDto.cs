using System.Text.Json;
using System.Text.Json.Serialization;

namespace COMTool.Core.Macros;

/// <summary>
/// JSON-friendly mirror of <see cref="MacroStep"/> for persistence in
/// config.json. The kind discriminator is stored as a string so that older
/// configs (without a "steps" array) keep deserializing unchanged.
/// </summary>
public sealed class MacroStepDto
{
    /// <summary>"text" | "key" | "sleep" | "wait".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Per-keystroke delay in ms for text steps (0 = send at once).</summary>
    [JsonPropertyName("keyDelayMs")]
    public int KeyDelayMs { get; set; }

    /// <summary>Key name for key steps (see <see cref="MacroKeys.All"/>).</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Duration in ms for sleep steps.</summary>
    [JsonPropertyName("ms")]
    public int Milliseconds { get; set; }

    /// <summary>Output pattern for wait steps.</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>Timeout in ms for wait steps (0 = default 30000).</summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 30000;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
