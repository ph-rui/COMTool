namespace COMTool.Core.Config;

public static class ConfigPaths
{
    public const string ApplicationName = "COMTool";
    public const string AvaloniaApplicationName = "COMTool-Avalonia";

    public static string PythonConfigDirectory() =>
        Override("COMTOOL_PYTHON_CONFIG_DIR")
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ApplicationName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "comtool"));

    public static string AvaloniaConfigDirectory() =>
        Override("COMTOOL_AVALONIA_CONFIG_DIR")
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AvaloniaApplicationName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "comtool-avalonia"));

    private static string? Override(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    public static string PythonConfigFile() =>
        Path.Combine(PythonConfigDirectory(), "config.json");

    public static string AvaloniaConfigFile() =>
        Path.Combine(AvaloniaConfigDirectory(), "config.json");

    public static string LogFile() =>
        Path.Combine(AvaloniaConfigDirectory(), "run.log");
}
