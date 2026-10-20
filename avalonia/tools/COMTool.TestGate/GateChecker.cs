namespace COMTool.TestGate;

public sealed record GateOptions(
    string Phase,
    string Platform,
    string ParityPath,
    string MatrixPath,
    string ManualPath,
    string TestListPath,
    string E2eLogPath,
    string PackageLogPath);

public static class GateChecker
{
    private const string Completed = "完成";
    private const string Partial = "部分";
    private const string NotStarted = "未开始";
    private const string NotApplicable = "不适用";
    private const string ManualPhase = "manual";
    private const string PackagePhase = "package";

    private static readonly string[] ManualReasonWords =
    {
        "不能自动化",
        "无法自动化",
        "不能",
        "无法",
        "硬件",
        "视觉",
        "真实桌面",
        "真实",
    };

    public static GateResult Check(GateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var parity = ParityMatrix.Load(options.ParityPath);
        var matrix = FeatureMatrix.Load(options.MatrixPath);
        var manualIds = ManualAcceptance.Load(options.ManualPath);

        var rowsByFeature = new Dictionary<string, FeatureRow>(StringComparer.Ordinal);
        foreach (var row in matrix)
        {
            if (string.IsNullOrWhiteSpace(row.Feature))
            {
                errors.Add("功能测试矩阵包含空的 Feature ID。");
                continue;
            }

            if (!rowsByFeature.TryAdd(row.Feature, row))
            {
                errors.Add($"功能测试矩阵包含重复的 Feature ID '{row.Feature}'。");
            }

            if (!parity.ContainsKey(row.Feature))
            {
                errors.Add(
                    $"功能测试矩阵引用了 parity 矩阵中不存在的 Feature ID '{row.Feature}'。");
            }
        }

        var testIds = ReadTestList(options.TestListPath);
        var e2eIds = ReadPassedIds(options.E2eLogPath);
        var packageIds = ReadPassedIds(options.PackageLogPath);

        foreach (var row in matrix)
        {
            if (!ShouldEnforce(row, options))
            {
                continue;
            }

            foreach (var manualId in row.Manual)
            {
                if (!manualIds.Contains(manualId))
                {
                    errors.Add(
                        $"功能 '{row.Feature}' 引用的人工验收项 '{manualId}' " +
                        $"不存在于人工验收文档 '{options.ManualPath}'。");
                }
            }
        }

        foreach (var (feature, status) in parity)
        {
            if (status is NotStarted or NotApplicable)
            {
                continue;
            }

            if (!rowsByFeature.TryGetValue(feature, out var row))
            {
                if (status is Completed or Partial)
                {
                    errors.Add(
                        $"功能 '{feature}' 状态为 {status}，但功能测试矩阵缺少该条目。");
                }
                else
                {
                    errors.Add($"功能 '{feature}' 的状态 '{status}' 无法识别。");
                }

                continue;
            }

            if (!ShouldEnforce(row, options))
            {
                continue;
            }

            switch (status)
            {
                case Completed:
                    EnforceCompleted(feature, row, options, testIds, e2eIds, packageIds, errors);
                    break;
                case Partial:
                    EnforcePartial(feature, row, options, testIds, e2eIds, packageIds, errors);
                    break;
                default:
                    errors.Add($"功能 '{feature}' 的状态 '{status}' 无法识别。");
                    break;
            }
        }

        return new GateResult(errors);
    }

    private static void EnforceCompleted(
        string feature,
        FeatureRow row,
        GateOptions options,
        IReadOnlySet<string> testIds,
        IReadOnlySet<string> e2eIds,
        IReadOnlySet<string> packageIds,
        List<string> errors)
    {
        if (row.Automated.Count == 0)
        {
            errors.Add($"功能 '{feature}' 状态为 完成，但没有自动化测试（无自动化测试）。");
            return;
        }

        EnforceAutomatedIds(feature, row, options, testIds, e2eIds, packageIds, errors);
    }

    private static void EnforcePartial(
        string feature,
        FeatureRow row,
        GateOptions options,
        IReadOnlySet<string> testIds,
        IReadOnlySet<string> e2eIds,
        IReadOnlySet<string> packageIds,
        List<string> errors)
    {
        if (row.Automated.Count == 0 && row.Manual.Count == 0)
        {
            errors.Add(
                $"功能 '{feature}' 状态为 部分，但既没有自动化测试也没有人工验收项。");
            return;
        }

        if (row.Automated.Count > 0)
        {
            EnforceAutomatedIds(feature, row, options, testIds, e2eIds, packageIds, errors);
            return;
        }

        if (!HasManualReason(row.Evidence))
        {
            errors.Add(
                $"功能 '{feature}' 仅有人工验收项 {string.Join(", ", row.Manual)}，" +
                "但 Evidence 未说明不能自动化的原因（需包含 " +
                $"{string.Join("、", ManualReasonWords)} 之一）。");
        }
    }

    private static void EnforceAutomatedIds(
        string feature,
        FeatureRow row,
        GateOptions options,
        IReadOnlySet<string> testIds,
        IReadOnlySet<string> e2eIds,
        IReadOnlySet<string> packageIds,
        List<string> errors)
    {
        var available = RowPhase(row) == PackagePhase
            ? testIds.Concat(packageIds)
            : testIds.Concat(e2eIds);

        var availableList = available.ToArray();
        foreach (var testId in row.Automated)
        {
            if (Resolves(testId, availableList))
            {
                continue;
            }

            errors.Add(
                $"功能 '{feature}' 引用的自动化测试 '{testId}' 不存在于测试列表或 " +
                $"{RowPhase(row)} 阶段日志中。");
        }
    }

    private static bool HasManualReason(string evidence)
    {
        var value = evidence.Trim();
        if (value.Length == 0 || value == "-")
        {
            return false;
        }

        return ManualReasonWords.Any(word => value.Contains(word, StringComparison.Ordinal));
    }

    private static bool ShouldEnforce(FeatureRow row, GateOptions options)
    {
        var phase = row.Phase.Trim();
        var optionPhase = options.Phase.Trim();

        // Phase=manual entries are only checked during manual sign-off; the
        // automated verify/package gate never requires them to be automated.
        if (string.Equals(phase, ManualPhase, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(optionPhase, ManualPhase, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(phase, optionPhase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var platform = row.Platform.Trim();
        return string.Equals(platform, "all", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(platform, options.Platform.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string RowPhase(FeatureRow row) =>
        row.Phase.Trim().ToLowerInvariant();

    private static bool Resolves(string id, IReadOnlyList<string> available)
    {
        foreach (var name in available)
        {
            if (string.Equals(name, id, StringComparison.Ordinal))
            {
                return true;
            }

            if (name.EndsWith($".{id}", StringComparison.Ordinal))
            {
                return true;
            }

            if ((id.StartsWith("COMTool.", StringComparison.Ordinal) ||
                 id.Contains("Tests.", StringComparison.Ordinal)) &&
                name.Contains(id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlySet<string> ReadTestList(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ReadLines(path))
        {
            ids.Add(line);
        }

        return ids;
    }

    private static IReadOnlySet<string> ReadPassedIds(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ReadLines(path))
        {
            var parts = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && string.Equals(parts[0], "PASS", StringComparison.Ordinal))
            {
                ids.Add(parts[1].TrimEnd(':'));
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> ReadLines(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
    }
}
