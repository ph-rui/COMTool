namespace COMTool.TestGate;

public sealed class FeatureRow
{
    public FeatureRow(
        string feature,
        string status,
        IReadOnlyList<string> automated,
        string phase,
        string platform,
        string command,
        IReadOnlyList<string> manual,
        string evidence)
    {
        Feature = feature;
        Status = status;
        Automated = automated;
        Phase = phase;
        Platform = platform;
        Command = command;
        Manual = manual;
        Evidence = evidence;
    }

    public string Feature { get; }

    public string Status { get; }

    public IReadOnlyList<string> Automated { get; }

    public string Phase { get; }

    public string Platform { get; }

    public string Command { get; }

    public IReadOnlyList<string> Manual { get; }

    public string Evidence { get; }
}

public static class FeatureMatrix
{
    private static readonly string[] RequiredColumns =
    {
        "Feature",
        "Status",
        "Automated",
        "Phase",
        "Platform",
        "Command",
        "Manual",
        "Evidence",
    };

    public static IReadOnlyList<FeatureRow> Load(string path)
    {
        var rows = MarkdownTable.Read(path);
        if (rows.Count == 0)
        {
            throw new InvalidDataException(
                $"Feature matrix '{path}' contains no data rows.");
        }

        var features = new List<FeatureRow>(rows.Count);

        foreach (var row in rows)
        {
            foreach (var column in RequiredColumns)
            {
                if (!row.ContainsKey(column))
                {
                    throw new InvalidDataException(
                        $"Feature matrix '{path}' is missing the required column '{column}'.");
                }
            }

            features.Add(new FeatureRow(
                row["Feature"],
                row["Status"],
                SplitIds(row["Automated"]),
                row["Phase"],
                row["Platform"],
                row["Command"],
                SplitIds(row["Manual"]),
                row["Evidence"]));
        }

        return features.AsReadOnly();
    }

    private static IReadOnlyList<string> SplitIds(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static id => id != "-")
            .ToArray();
}
