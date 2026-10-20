using System.Text.RegularExpressions;

namespace COMTool.TestGate;

public static class TestLog
{
    private static readonly Regex PassPattern = new(
        @"^PASS\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] NoisePrefixes =
    {
        "Microsoft (R)",
        "Copyright",
        "Test run for ",
        "Starting test execution",
        "A total of ",
        "Test Run ",
        "Results File:",
        "Passed!",
        "Failed!",
        "No test matches ",
    };

    /// <summary>
    /// Reads test IDs from <c>dotnet test --list-tests</c> output. Missing files
    /// are treated as empty optional sources.
    /// </summary>
    public static IReadOnlySet<string> ReadTests(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in ReadLines(path))
        {
            var line = raw.Trim();
            if (!IsTestName(line))
            {
                continue;
            }

            ids.Add(line);
        }

        return ids;
    }

    /// <summary>
    /// Reads IDs from <c>PASS &lt;id&gt;</c> lines. <c>FAIL</c> and unrelated
    /// output are ignored. Missing files are treated as empty optional sources.
    /// </summary>
    public static IReadOnlySet<string> ReadPassed(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in ReadLines(path))
        {
            var match = PassPattern.Match(raw.Trim());
            if (!match.Success)
            {
                continue;
            }

            ids.Add(match.Groups[1].Value.TrimEnd(':'));
        }

        return ids;
    }

    private static string[] ReadLines(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllLines(path);
    }

    private static bool IsTestName(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        // "The following Tests are available:" and similar section headers.
        if (line.EndsWith(':'))
        {
            return false;
        }

        foreach (var prefix in NoisePrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return line.Any(char.IsLetterOrDigit);
    }
}
