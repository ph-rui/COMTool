namespace COMTool.TestGate;

public static class ManualAcceptance
{
    public static IReadOnlySet<string> Load(string path)
    {
        var rows = MarkdownTable.Read(path);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var foundIdColumn = false;

        foreach (var row in rows)
        {
            if (!row.TryGetValue("ID", out var value))
            {
                continue;
            }

            foundIdColumn = true;
            var id = value.Trim();
            if (id.Length == 0 || id == "-")
            {
                continue;
            }

            ids.Add(id);
        }

        if (rows.Count > 0 && !foundIdColumn)
        {
            throw new InvalidDataException(
                $"Manual acceptance '{path}' is missing the required column 'ID'.");
        }

        return ids;
    }
}
