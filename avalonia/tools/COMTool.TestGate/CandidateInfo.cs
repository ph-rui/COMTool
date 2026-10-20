using System.Text.Json;

namespace COMTool.TestGate;

/// <summary>
/// Machine-readable metadata written next to a candidate package. Property
/// names are serialized in camelCase so the on-disk contract stays stable for
/// the packaging scripts and the sign-off validator.
/// </summary>
public sealed record CandidateInfo(
    string Version,
    string Platform,
    string Sha256,
    string BuiltAt,
    string Commit,
    string ResultsDir,
    int Passed,
    int Failed,
    int Skipped)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CandidateInfo? FromJson(string json) =>
        JsonSerializer.Deserialize<CandidateInfo>(json, JsonOptions);
}
