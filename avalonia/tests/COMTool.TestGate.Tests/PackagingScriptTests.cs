using System.Text.RegularExpressions;

namespace COMTool.TestGate.Tests;

public sealed class PackagingScriptTests
{
    [Fact]
    public void PK01_WindowsPackResolvesDefaultsFromScriptBody()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "avalonia", "pack", "windows", "pack.ps1");
        var text = File.ReadAllText(path);
        var match = Regex.Match(
            text,
            @"(?s)\bparam\s*\((.*?)\)",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "pack.ps1 param block was not found");
        Assert.DoesNotContain("$PSScriptRoot", match.Groups[1].Value);
        Assert.Contains("if (-not $Source)", text);
        Assert.Contains("if (-not $CandidateDir)", text);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "parity-matrix.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root was not found");
    }
}

public sealed class PackagingGateScriptTests
{
    [Fact]
    public void PK02_VerifyRetriesIntermittentTestListFailure()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "avalonia", "pack", "verify.ps1");
        var text = File.ReadAllText(path);

        Assert.Contains("$listAttempts", text);
        Assert.Contains("retrying", text);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "parity-matrix.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root was not found");
    }
}
