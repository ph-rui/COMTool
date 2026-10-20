using Avalonia;
using Avalonia.Headless;
using COMTool.App;

[assembly: AvaloniaTestApplication(typeof(COMTool.App.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace COMTool.App.Tests;

public static class TestAppBuilder
{
    static TestAppBuilder()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AvaloniaConfigDir);
        Directory.CreateDirectory(PythonConfigDir);
        Environment.SetEnvironmentVariable("COMTOOL_AVALONIA_CONFIG_DIR", AvaloniaConfigDir);
        Environment.SetEnvironmentVariable("COMTOOL_PYTHON_CONFIG_DIR", PythonConfigDir);
    }

    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "comtool-app-tests", Guid.NewGuid().ToString("N"));

    public static string AvaloniaConfigDir => Path.Combine(Root, "avalonia");

    public static string PythonConfigDir => Path.Combine(Root, "python");

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
