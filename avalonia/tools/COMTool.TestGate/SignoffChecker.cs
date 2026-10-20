using System.Text.Json;
using System.Text.RegularExpressions;

namespace COMTool.TestGate;

/// <summary>
/// Manual acceptance record for one candidate package. The JSON contract is
/// camelCase, matching candidate.json and the sign-off templates.
/// </summary>
public sealed record Signoff(
    string Product,
    string Version,
    string Platform,
    string CandidateSha256,
    string Tester,
    string Date,
    string WindowsVersion,
    IReadOnlyList<string> Hardware,
    IReadOnlyList<SignoffItem> Items,
    Soak Soak)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static Signoff? FromJson(string json) =>
        JsonSerializer.Deserialize<Signoff>(json, JsonOptions);
}

public sealed record SignoffItem(string Id, string Result, string Evidence);

public sealed record Soak(int Hours, string Result, string Evidence);

/// <summary>
/// Enforces the release sign-off contract. A candidate can only be promoted
/// when the sign-off is bound to its exact SHA256, covers every manual item in
/// the feature matrix, contains only PASS results and records an 8 hour soak.
/// </summary>
public static class SignoffChecker
{
    private const string ProductName = "COMTool-Avalonia";
    private const int RequiredSoakHours = 8;

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(
        CandidateInfo candidate,
        Signoff signoff,
        IReadOnlySet<string> requiredManualIds) =>
        Validate(candidate, signoff, requiredManualIds, warnings: null);

    /// <summary>
    /// Validates a sign-off and records non-fatal evidence warnings when a
    /// warning collection is supplied. Missing evidence files are warnings;
    /// empty evidence is always an error.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        CandidateInfo candidate,
        Signoff signoff,
        IReadOnlySet<string> requiredManualIds,
        ICollection<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(signoff);
        ArgumentNullException.ThrowIfNull(requiredManualIds);

        var errors = new List<string>();
        var candidateSha = candidate.Sha256 ?? string.Empty;
        var signoffSha = signoff.CandidateSha256 ?? string.Empty;

        if (!string.Equals(signoff.Product, ProductName, StringComparison.Ordinal))
        {
            errors.Add(
                $"Signoff product '{signoff.Product ?? "<missing>"}' must be '{ProductName}'.");
        }

        if (!string.Equals(signoff.Version, candidate.Version, StringComparison.Ordinal))
        {
            errors.Add(
                $"Signoff version '{signoff.Version ?? "<missing>"}' does not match candidate version " +
                $"'{candidate.Version ?? "<missing>"}'.");
        }

        if (!string.Equals(signoff.Platform, candidate.Platform, StringComparison.Ordinal))
        {
            errors.Add(
                $"Signoff platform '{signoff.Platform ?? "<missing>"}' does not match candidate platform " +
                $"'{candidate.Platform ?? "<missing>"}'.");
        }

        if (!Sha256Pattern.IsMatch(candidateSha))
        {
            errors.Add(
                $"Candidate SHA256 '{candidateSha}' must be exactly 64 hexadecimal characters.");
        }

        if (!Sha256Pattern.IsMatch(signoffSha))
        {
            errors.Add(
                $"Signoff SHA256 '{signoffSha}' must be exactly 64 hexadecimal characters.");
        }

        if (Sha256Pattern.IsMatch(candidateSha) &&
            Sha256Pattern.IsMatch(signoffSha) &&
            !string.Equals(candidateSha, signoffSha, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"SHA256 mismatch: candidate '{candidateSha}' does not match signoff " +
                $"'{signoffSha}'.");
        }

        var items = signoff.Items ?? Array.Empty<SignoffItem>();
        var requiredItems = items
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(static item => item.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        foreach (var requiredId in requiredManualIds)
        {
            if (!requiredItems.TryGetValue(requiredId, out var count))
            {
                errors.Add($"Missing required manual item '{requiredId}'.");
            }
            else if (count > 1)
            {
                errors.Add($"Required manual item '{requiredId}' appears more than once.");
            }
        }

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id))
            {
                errors.Add("Every signoff item must have a non-empty ID.");
                continue;
            }

            var result = (item.Result ?? string.Empty).Trim();
            if (string.Equals(result, "FAIL", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Manual item '{item.Id}' result is FAIL; every item must be PASS.");
            }
            else if (!string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Manual item '{item.Id}' result '{item.Result ?? "<missing>"}' is not PASS.");
            }

            var evidence = item.Evidence ?? string.Empty;
            if (string.IsNullOrWhiteSpace(evidence))
            {
                errors.Add($"Manual item '{item.Id}' has empty evidence.");
            }
            else
            {
                WarnIfEvidenceNotFound(item.Id, evidence, warnings);
            }
        }

        if (signoff.Soak is null)
        {
            errors.Add(
                $"Soak record is missing; at least {RequiredSoakHours} hours with PASS and evidence is required.");
        }
        else
        {
            var soak = signoff.Soak;
            if (soak.Hours < RequiredSoakHours)
            {
                errors.Add(
                    $"Soak duration {soak.Hours}h is shorter than the required {RequiredSoakHours} hours.");
            }

            var soakResult = (soak.Result ?? string.Empty).Trim();
            if (string.Equals(soakResult, "FAIL", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Soak result is FAIL; the required {RequiredSoakHours} hour soak must PASS.");
            }
            else if (!string.Equals(soakResult, "PASS", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Soak result '{soak.Result ?? "<missing>"}' is not PASS; " +
                    $"the required {RequiredSoakHours} hour soak must PASS.");
            }

            if (string.IsNullOrWhiteSpace(soak.Evidence))
            {
                errors.Add($"Soak has empty evidence; attach evidence for the {RequiredSoakHours} hour run.");
            }
            else
            {
                WarnIfEvidenceNotFound("soak", soak.Evidence, warnings);
            }
        }

        return errors.AsReadOnly();
    }

    private static void WarnIfEvidenceNotFound(
        string id,
        string evidence,
        ICollection<string>? warnings)
    {
        if (warnings is null || IsUrl(evidence))
        {
            return;
        }

        var paths = new List<string> { evidence };
        var root = FindRepositoryRoot();
        if (root is not null && !Path.IsPathRooted(evidence))
        {
            paths.Add(Path.Combine(root, evidence));
        }

        if (paths.Any(path => File.Exists(path) || Directory.Exists(path)))
        {
            return;
        }

        warnings.Add(
            $"Evidence '{evidence}' for '{id}' was not found; verify it is attached before release.");
    }

    private static bool IsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "parity-matrix.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "avalonia")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
