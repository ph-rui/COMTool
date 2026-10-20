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
        string[]? headers = null;
        var rows = new List<IReadOnlyDictionary<string, string>>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitCells(line);
            if (IsSeparatorRow(cells))
            {
                continue;
            }

            if (headers is null)
            {
                headers = cells;
                if (headers.Length == 0 || headers.All(string.IsNullOrWhiteSpace))
                {
                    throw new InvalidDataException(
                        $"Markdown table '{path}' has no columns in its header row at line {index + 1}.");
                }

                continue;
            }

            if (cells.Length != headers.Length)
            {
                throw new InvalidDataException(
                    $"Markdown table '{path}' row at line {index + 1} has {cells.Length} cell(s), " +
                    $"but the header has {headers.Length}.");
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < headers.Length; column++)
            {
                if (!row.TryAdd(headers[column], cells[column]))
                {
                    throw new InvalidDataException(
                        $"Markdown table '{path}' has a duplicate header '{headers[column]}' at line {index + 1}.");
                }
            }

            rows.Add(new ReadOnlyDictionary<string, string>(row));
        }

        if (headers is null)
        {
            throw new InvalidDataException($"Markdown table '{path}' does not contain a header row.");
        }

        return rows.AsReadOnly();
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
