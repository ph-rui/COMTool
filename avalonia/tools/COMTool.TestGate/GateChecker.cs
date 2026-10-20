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

    private const string VerifyPhase = "verify";
    private const string PackagePhase = "package";
    private const string ManualPhase = "manual";
    private const string AllPlatform = "all";

    private static readonly HashSet<string> KnownStatuses =
        new(StringComparer.Ordinal) { Completed, Partial, NotStarted, NotApplicable };

    private static readonly HashSet<string> KnownPhases =
        new(StringComparer.OrdinalIgnoreCase) { VerifyPhase, PackagePhase, ManualPhase };

    private static readonly HashSet<string> KnownPlatforms =
        new(StringComparer.OrdinalIgnoreCase) { AllPlatform, "linux", "windows", "macos" };

    private static readonly HashSet<string> OptionPhases =
        new(StringComparer.OrdinalIgnoreCase) { VerifyPhase, PackagePhase };

    private static readonly HashSet<string> OptionPlatforms =
        new(StringComparer.OrdinalIgnoreCase) { "linux", "windows", "macos" };

    private static readonly string[] ManualReasonPhrases =
    {
        "不能自动化",
        "无法自动化",
        "硬件",
        "视觉",
        "真实桌面",
    };

    public static GateResult Check(GateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var optionPhase = (options.Phase ?? string.Empty).Trim();
        var optionPlatform = (options.Platform ?? string.Empty).Trim();

        // Validate the selected gate stage before any file or row filtering.
        if (!OptionPhases.Contains(optionPhase))
        {
            errors.Add(
                $"--phase '{options.Phase}' 无效；允许值为 verify、package。");
        }

        if (!OptionPlatforms.Contains(optionPlatform))
        {
            errors.Add(
                $"--platform '{options.Platform}' 无效；允许值为 linux、windows、macos。");
        }

        var parity = ParityMatrix.Load(options.ParityPath);
        var matrix = FeatureMatrix.Load(options.MatrixPath);
        var manualIds = ManualAcceptance.Load(options.ManualPath);

        foreach (var (feature, status) in parity)
        {
            if (!KnownStatuses.Contains(status))
            {
                errors.Add($"功能 '{feature}' 的状态 '{status}' 无法识别。");
            }
        }

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

            // Phase and platform are validated before any filtering so an
            // invalid row cannot silently escape enforcement.
            if (!KnownPhases.Contains(row.Phase.Trim()))
            {
                errors.Add(
                    $"功能 '{row.Feature}' 的功能测试矩阵 Phase '{row.Phase}' 无效。");
            }

            if (!KnownPlatforms.Contains(row.Platform.Trim()))
            {
                errors.Add(
                    $"功能 '{row.Feature}' 的功能测试矩阵 Platform '{row.Platform}' 无效。");
            }

            if (string.Equals(row.Phase.Trim(), ManualPhase, StringComparison.OrdinalIgnoreCase) &&
                row.Automated.Count > 0)
            {
                errors.Add(
                    $"功能 '{row.Feature}' 的 Phase=manual 行不能声明 Automated 测试" +
                    $"（{string.Join(", ", row.Automated)}）。");
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

        var testIds = ReadTestList(options.TestListPath);
        var e2eIds = ReadPassedIds(options.E2eLogPath);
        var packageIds = ReadPassedIds(options.PackageLogPath);

        foreach (var (feature, status) in parity)
        {
            if (!rowsByFeature.TryGetValue(feature, out var row))
            {
                errors.Add(
                    $"功能 '{feature}' 状态为 {status}，但功能测试矩阵缺少该条目。");
                continue;
            }

            if (!KnownStatuses.Contains(status))
            {
                continue;
            }

            EnforceStructure(feature, status, row, errors);

            if (row.Automated.Count > 0 && ShouldResolve(row, optionPhase, optionPlatform))
            {
                EnforceAutomatedIds(
                    feature, row, testIds, e2eIds, packageIds, errors);
            }
        }

        return new GateResult(errors);
    }

    private static void EnforceStructure(
        string feature,
        string status,
        FeatureRow row,
        List<string> errors)
    {
        switch (status)
        {
            case Completed:
                if (row.Automated.Count == 0)
                {
                    errors.Add(
                        $"功能 '{feature}' 状态为 完成，但没有自动化测试（无自动化测试）；" +
                        "人工验收或 Phase=manual 不能替代自动化测试。");
                }

                break;

            case Partial:
                if (row.Automated.Count == 0 && row.Manual.Count == 0)
                {
                    errors.Add(
                        $"功能 '{feature}' 状态为 部分，但既没有自动化测试也没有人工验收项。");
                }
                else if (row.Automated.Count == 0 && !HasManualReason(row.Evidence))
                {
                    errors.Add(
                        $"功能 '{feature}' 仅有人工验收项 {string.Join(", ", row.Manual)}，" +
                        "但 Evidence 未说明不能自动化的原因（需包含 " +
                        $"{string.Join("、", ManualReasonPhrases)} 之一）。");
                }

                break;
        }
    }

    private static void EnforceAutomatedIds(
        string feature,
        FeatureRow row,
        IReadOnlySet<string> testIds,
        IReadOnlySet<string> e2eIds,
        IReadOnlySet<string> packageIds,
        List<string> errors)
    {
        var available = IsPackageRow(row)
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
                $"功能 '{feature}' 引用的自动化测试 '{testId}' " +
                "不存在于测试列表或对应阶段日志中。");
        }
    }

    private static bool HasManualReason(string evidence)
    {
        var value = evidence.Trim();
        if (value.Length == 0 || value == "-")
        {
            return false;
        }

        return ManualReasonPhrases.Any(
            phrase => value.Contains(phrase, StringComparison.Ordinal));
    }

    private static bool ShouldResolve(FeatureRow row, string optionPhase, string optionPlatform)
    {
        var phase = row.Phase.Trim();

        // Manual rows are structural only and must not declare automated IDs;
        // their manual evidence is checked by the sign-off validator.
        if (string.Equals(phase, ManualPhase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(phase, optionPhase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var platform = row.Platform.Trim();
        return string.Equals(platform, AllPlatform, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(platform, optionPlatform, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackageRow(FeatureRow row) =>
        string.Equals(row.Phase.Trim(), PackagePhase, StringComparison.OrdinalIgnoreCase);

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
