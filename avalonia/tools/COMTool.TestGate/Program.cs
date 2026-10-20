using System.Runtime.InteropServices;
using System.Text.Json;

namespace COMTool.TestGate;

public static class Program
{
    private const string DefaultPhase = "verify";

    public static int Main(string[] args)
    {
        if (args.Any(static argument => argument == "--validate-signoff"))
        {
            return RunSignoffValidation(args);
        }

        return RunGate(args);
    }

    private static int RunGate(string[] args)
    {
        var errors = new List<string>();
        var options = ParseOptions(args, errors);

        if (options is not null)
        {
            RequireFile("parity 矩阵", options.ParityPath, errors);
            RequireFile("功能测试矩阵", options.MatrixPath, errors);
            RequireFile("人工验收文档", options.ManualPath, errors);

            if (errors.Count == 0)
            {
                try
                {
                    errors.AddRange(GateChecker.Check(options).Errors);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    InvalidDataException or
                    UnauthorizedAccessException or
                    ArgumentException)
                {
                    errors.Add(exception.Message);
                }
            }
        }

        foreach (var error in errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("RESULT: PASS");
            return 0;
        }

        Console.WriteLine($"RESULT: FAIL ({errors.Count})");
        return 1;
    }

    private static int RunSignoffValidation(string[] args)
    {
        var errors = new List<string>();
        string? candidatePath = null;
        string? signoffPath = null;
        string? manualPath = null;
        string? matrixPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--validate-signoff")
            {
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                errors.Add($"Unrecognized argument '{argument}'.");
                continue;
            }

            if (index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Option '{argument}' is missing a value.");
                continue;
            }

            var value = args[++index];
            switch (argument[2..])
            {
                case "candidate":
                    candidatePath = value;
                    break;
                case "signoff":
                    signoffPath = value;
                    break;
                case "manual":
                    manualPath = value;
                    break;
                case "matrix":
                    matrixPath = value;
                    break;
                default:
                    errors.Add($"Unknown option '{argument}'.");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            errors.Add("--candidate <json> is required.");
        }

        if (string.IsNullOrWhiteSpace(signoffPath))
        {
            errors.Add("--signoff <json> is required.");
        }

        var root = FindRepositoryRoot() ?? Environment.CurrentDirectory;
        manualPath ??= Path.Combine(root, "docs", "testing", "manual-acceptance.md");
        matrixPath ??= Path.Combine(root, "docs", "testing", "feature-test-matrix.md");

        CandidateInfo? candidate = null;
        Signoff? signoff = null;
        IReadOnlyList<FeatureRow>? features = null;
        IReadOnlySet<string>? manualIds = null;

        if (errors.Count == 0)
        {
            candidate = LoadJson(
                "Candidate JSON",
                candidatePath!,
                CandidateInfo.FromJson,
                errors);
            signoff = LoadJson(
                "Signoff JSON",
                signoffPath!,
                Signoff.FromJson,
                errors);

            try
            {
                features = FeatureMatrix.Load(matrixPath);
                manualIds = ManualAcceptance.Load(manualPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                errors.Add(exception.Message);
            }

            if (features is not null)
            {
                var requiredIds = features
                    .SelectMany(static feature => feature.Manual)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();

                if (manualIds is not null)
                {
                    foreach (var requiredId in requiredIds)
                    {
                        if (!manualIds.Contains(requiredId))
                        {
                            errors.Add(
                                $"Manual item '{requiredId}' from the feature matrix is missing from " +
                                $"'{manualPath}'.");
                        }
                    }
                }

                if (candidate is not null && signoff is not null)
                {
                    var warnings = new List<string>();
                    errors.AddRange(SignoffChecker.Validate(
                        candidate,
                        signoff,
                        requiredIds.ToHashSet(StringComparer.Ordinal),
                        warnings));

                    foreach (var warning in warnings)
                    {
                        Console.WriteLine($"WARNING: {warning}");
                    }
                }
            }
        }

        foreach (var error in errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("RESULT: PASS");
            return 0;
        }

        Console.WriteLine($"RESULT: FAIL ({errors.Count})");
        return 1;
    }

    private static T? LoadJson<T>(
        string label,
        string path,
        Func<string, T?> deserialize,
        List<string> errors)
        where T : class
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} '{path}' does not exist.");
            return null;
        }

        try
        {
            var value = deserialize(File.ReadAllText(path));
            if (value is null)
            {
                errors.Add($"{label} '{path}' is empty or literal null.");
            }

            return value;
        }
        catch (JsonException exception)
        {
            errors.Add($"{label} '{path}' is not valid JSON: {exception.Message}");
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add($"{label} '{path}' could not be read: {exception.Message}");
            return null;
        }
    }

    private static GateOptions? ParseOptions(string[] args, List<string> errors)
    {
        var initialErrorCount = errors.Count;
        string? phase = null;
        string? platform = null;
        string? parityPath = null;
        string? matrixPath = null;
        string? manualPath = null;
        string? testListPath = null;
        string? e2eLogPath = null;
        string? packageLogPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                errors.Add($"无法识别的参数 '{argument}'。");
                continue;
            }

            if (index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"选项 '{argument}' 缺少值。");
                continue;
            }

            var value = args[++index];
            switch (argument[2..])
            {
                case "phase":
                    phase = value;
                    break;
                case "platform":
                    platform = value;
                    break;
                case "parity":
                    parityPath = value;
                    break;
                case "matrix":
                    matrixPath = value;
                    break;
                case "manual":
                    manualPath = value;
                    break;
                case "tests":
                    testListPath = value;
                    break;
                case "e2e":
                    e2eLogPath = value;
                    break;
                case "package":
                    packageLogPath = value;
                    break;
                default:
                    errors.Add($"未知选项 '{argument}'。");
                    break;
            }
        }

        if (errors.Count != initialErrorCount)
        {
            return null;
        }

        var root = FindRepositoryRoot() ?? Environment.CurrentDirectory;

        return new GateOptions(
            phase ?? DefaultPhase,
            platform ?? DetectPlatform(),
            parityPath ?? Path.Combine(root, "docs", "parity-matrix.md"),
            matrixPath ?? Path.Combine(root, "docs", "testing", "feature-test-matrix.md"),
            manualPath ?? Path.Combine(root, "docs", "testing", "manual-acceptance.md"),
            testListPath ?? string.Empty,
            e2eLogPath ?? string.Empty,
            packageLogPath ?? string.Empty);
    }

    private static void RequireFile(string label, string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} '{path}' 不存在；请通过对应的命令行选项提供有效路径。");
        }
    }

    private static string DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macos";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        return RuntimeInformation.OSDescription;
    }

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
