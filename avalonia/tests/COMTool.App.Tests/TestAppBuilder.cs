using Avalonia;
using Avalonia.Headless;
using COMTool.App;

[assembly: AvaloniaTestApplication(typeof(COMTool.App.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace COMTool.App.Tests;

public static class TestAppBuilder
{
    private static int _failureRetained;

    static TestAppBuilder()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AvaloniaConfigDir);
        Directory.CreateDirectory(PythonConfigDir);
        Environment.SetEnvironmentVariable("COMTOOL_AVALONIA_CONFIG_DIR", AvaloniaConfigDir);
        Environment.SetEnvironmentVariable("COMTOOL_PYTHON_CONFIG_DIR", PythonConfigDir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupSharedRoot();
    }

    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "comtool-app-tests", Guid.NewGuid().ToString("N"));

    public static string AvaloniaConfigDir => Path.Combine(Root, "avalonia");

    public static string PythonConfigDir => Path.Combine(Root, "python");

    internal static void MarkFailureRetained() =>
        Interlocked.Exchange(ref _failureRetained, 1);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    private static void CleanupSharedRoot()
    {
        var retained = Volatile.Read(ref _failureRetained) != 0 || HasRetainedTestEvidence();
        if (retained)
        {
            Console.WriteLine($"[COMTool.App.Tests] retained shared test root: {Root}");
            return;
        }

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[COMTool.App.Tests] failed to delete shared root {Root}: {ex.Message}");
        }
    }

    private static bool HasRetainedTestEvidence()
    {
        var testsDirectory = Path.Combine(Root, "tests");
        return Directory.Exists(testsDirectory)
            && Directory.EnumerateDirectories(testsDirectory).Any();
    }
}
