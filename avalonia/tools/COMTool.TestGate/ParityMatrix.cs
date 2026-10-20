using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace COMTool.TestGate;

public static class ParityMatrix
{
    private static readonly Regex FeatureIdPattern =
        new("^[A-Z]+-\\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] KnownStatuses =
    {
        "未开始",
        "部分",
        "完成",
        "不适用",
    };

    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        var rows = MarkdownTable.Read(path);
        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!row.TryGetValue("ID", out var id))
            {
                throw new InvalidDataException($"Parity matrix '{path}' is missing the required column 'ID'.");
            }

            if (!row.TryGetValue("状态", out var rawStatus))
            {
                throw new InvalidDataException($"Parity matrix '{path}' is missing the required column '状态'.");
            }

            id = id.Trim();
            if (!FeatureIdPattern.IsMatch(id))
            {
                continue;
            }

            if (!statuses.TryAdd(id, NormalizeStatus(rawStatus)))
            {
                throw new InvalidDataException(
                    $"Parity matrix '{path}' contains duplicate feature ID '{id}'.");
            }
        }

        return new ReadOnlyDictionary<string, string>(statuses);
    }

    private static string NormalizeStatus(string value)
    {
        var status = value.Trim();
        foreach (var knownStatus in KnownStatuses)
        {
            if (status.StartsWith(knownStatus, StringComparison.Ordinal))
            {
                return knownStatus;
            }
        }

        return status;
    }
}
