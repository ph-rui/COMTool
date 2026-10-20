using System.Runtime.InteropServices;

namespace COMTool.TestGate;

public static class Program
{
    private const string DefaultPhase = "verify";

    public static int Main(string[] args)
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
