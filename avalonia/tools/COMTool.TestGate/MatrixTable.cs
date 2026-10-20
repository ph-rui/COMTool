using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace COMTool.TestGate;

public static class MarkdownTable
{
    private static readonly Regex SeparatorCellPattern =
        new("^:?-{3,}:?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = File.ReadAllLines(path);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        string[]? headers = null;
        string[]? pendingRow = null;
        var pendingRowLine = 0;

        void CommitPendingRow()
        {
            if (pendingRow is null)
            {
                return;
            }

            if (headers is not null)
            {
                if (pendingRow.Length != headers.Length)
                {
                    throw new InvalidDataException(
                        $"Markdown table '{path}' row at line {pendingRowLine} has {pendingRow.Length} cell(s), " +
                        $"but the header has {headers.Length}.");
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var column = 0; column < headers.Length; column++)
                {
                    row[headers[column]] = pendingRow[column];
                }

                rows.Add(new ReadOnlyDictionary<string, string>(row));
            }

            pendingRow = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.TrimStart().StartsWith('|'))
            {
                // Headings and prose terminate the current table. A later pipe row
                // starts a new table and is confirmed by its separator row.
                CommitPendingRow();
                headers = null;
                continue;
            }

            var cells = SplitCells(line);
            if (IsSeparatorRow(cells))
            {
                if (pendingRow is not null)
                {
                    headers = ValidateHeader(pendingRow, path, pendingRowLine);
                    pendingRow = null;
                }

                continue;
            }

            CommitPendingRow();
            pendingRow = cells;
            pendingRowLine = index + 1;
        }

        CommitPendingRow();

        if (headers is null && rows.Count == 0)
        {
            throw new InvalidDataException($"Markdown table '{path}' does not contain a header row.");
        }

        return rows.AsReadOnly();
    }

    private static string[] ValidateHeader(string[] cells, string path, int lineNumber)
    {
        if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                $"Markdown table '{path}' has no columns in its header row at line {lineNumber}.");
        }

        var duplicate = cells
            .GroupBy(static cell => cell, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Markdown table '{path}' has a duplicate header '{duplicate.Key}' at line {lineNumber}.");
        }

        return cells;
    }

    private static string[] SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Split('|').Select(static cell => cell.Trim()).ToArray();
    }

    private static bool IsSeparatorRow(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(cell => SeparatorCellPattern.IsMatch(cell));
}
