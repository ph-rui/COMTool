using System.Text.Json;

namespace COMTool.TestGate.Tests;

public sealed class CandidateInfoTests
{
    [Fact]
    public void CI01_SerializesCandidateInfo()
    {
        var candidate = new CandidateInfo(
            "0.01",
            "linux-x64",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "2026-10-20T12:34:56Z",
            "deadbeefcafe",
            "/tmp/comtool/test-results/20261020T123456Z",
            154,
            0,
            0);

        var json = candidate.ToJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(candidate.Sha256, root.GetProperty("sha256").GetString());
        Assert.Equal(candidate.BuiltAt, root.GetProperty("builtAt").GetString());
        Assert.Equal(candidate.Commit, root.GetProperty("commit").GetString());
        Assert.Equal(candidate.ResultsDir, root.GetProperty("resultsDir").GetString());
        Assert.Equal(candidate.Passed, root.GetProperty("passed").GetInt32());
        Assert.Equal(candidate.Skipped, root.GetProperty("skipped").GetInt32());

        // The on-disk contract is camelCase; PascalCase names must not appear.
        Assert.False(root.TryGetProperty("Sha256", out _));
        Assert.False(root.TryGetProperty("BuiltAt", out _));

        var roundTripped = CandidateInfo.FromJson(json);
        Assert.Equal(candidate, roundTripped);
    }
}
