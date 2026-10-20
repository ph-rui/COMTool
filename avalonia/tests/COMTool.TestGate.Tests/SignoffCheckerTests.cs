namespace COMTool.TestGate.Tests;

public sealed class SignoffCheckerTests
{
    [Fact]
    public void SO01_RejectsShaMismatch()
    {
        var errors = Validate(candidateSha: "aaaa", signoffSha: "bbbb", items: AllPass(), soak: Pass());
        Assert.Contains(errors, e => e.Contains("SHA256"));
    }

    [Fact]
    public void SO02_RejectsMissingManualItem()
    {
        var errors = Validate(items: new[] { Item("WIN-01") }, required: new[] { "WIN-01", "WIN-03" });
        Assert.Contains(errors, e => e.Contains("WIN-03"));
    }

    [Fact]
    public void SO03_RejectsFailResult()
    {
        var errors = Validate(items: new[] { Item("WIN-01", "FAIL") }, required: new[] { "WIN-01" });
        Assert.Contains(errors, e => e.Contains("FAIL"));
    }

    [Fact]
    public void SO04_RejectsEmptyEvidence()
    {
        var errors = Validate(items: new[] { Item("WIN-01", "PASS", "") });
        Assert.Contains(errors, e => e.Contains("evidence"));
    }

    [Fact]
    public void SO05_RejectsShortSoak()
    {
        var errors = Validate(soak: new Soak(4, "PASS", "x"));
        Assert.Contains(errors, e => e.Contains("8"));
    }

    [Fact]
    public void SO06_AcceptsCompleteSignoff()
    {
        var sha = new string('a', 64);
        Assert.Empty(Validate(candidateSha: sha, signoffSha: sha, items: AllPass(), soak: Pass()));
    }

    [Fact]
    public void SO07_RejectsDuplicateRequiredItem()
    {
        var errors = Validate(
            items: new[] { Item("WIN-01"), Item("WIN-01") },
            required: new[] { "WIN-01" });

        Assert.Contains(errors, e => e.Contains("WIN-01") && e.Contains("more than once"));
    }

    [Fact]
    public void SO08_RejectsProductVersionAndPlatformMismatch()
    {
        var productErrors = Validate(product: "COMTool");
        Assert.Contains(productErrors, e => e.Contains("product") && e.Contains("COMTool-Avalonia"));

        var versionErrors = Validate(version: "0.02");
        Assert.Contains(versionErrors, e => e.Contains("version"));

        var platformErrors = Validate(platform: "linux-x64");
        Assert.Contains(platformErrors, e => e.Contains("platform"));
    }

    [Fact]
    public void SO09_RejectsMalformedSha()
    {
        var errors = Validate(candidateSha: "not-a-sha", signoffSha: "not-a-sha");
        Assert.Contains(errors, e => e.Contains("SHA256"));
    }

    [Fact]
    public void SO10_RejectsEmptySoakEvidence()
    {
        var errors = Validate(soak: new Soak(8, "PASS", ""));
        Assert.Contains(errors, e => e.Contains("evidence"));
    }

    private static IReadOnlyList<string> Validate(
        string candidateSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string signoffSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        IReadOnlyList<SignoffItem>? items = null,
        Soak? soak = null,
        IReadOnlyList<string>? required = null,
        string product = "COMTool-Avalonia",
        string version = "0.01",
        string platform = "windows-x64") =>
        SignoffChecker.Validate(
            new CandidateInfo("0.01", "windows-x64", candidateSha, "2026-10-20T00:00:00Z", "abc123", "/tmp/results", 100, 0, 0),
            new Signoff(product, version, platform, signoffSha, "tester", "2026-10-20", "Windows 11", Array.Empty<string>(), items ?? AllPass(), soak ?? Pass()),
            (required ?? new[] { "WIN-01" }).ToHashSet());

    private static IReadOnlyList<SignoffItem> AllPass() =>
        new[] { Item("WIN-01") };

    private static SignoffItem Item(
        string id,
        string result = "PASS",
        string evidence = "docs/testing/evidence/WIN-01.png") =>
        new(id, result, evidence);

    private static Soak Pass() =>
        new(8, "PASS", "docs/testing/evidence/soak.txt");
}
