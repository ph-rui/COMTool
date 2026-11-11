using System.Diagnostics;

namespace COMTool.App.Services;

/// <summary>
/// Launches local files with programs (r60). The real implementation asks
/// the operating system; tests inject a recorder that implements this.
/// </summary>
public interface ILocalFileLauncher
{
    /// <summary>Opens the file with the operating system default program.</summary>
    bool OpenWithDefaultProgram(string filePath);

    /// <summary>Opens the file with the given program.</summary>
    bool OpenWithProgram(string programPath, string filePath);
}

/// <summary>
/// The launcher that asks the operating system. "Default program" is the
/// shell association of the file type; the text editor is notepad on
/// Windows, which is the system default text editor behind MobaXterm's
/// "open with the default text editor".
/// </summary>
public sealed class SystemLocalFileLauncher : ILocalFileLauncher
{
    /// <summary>
    /// The program that "open with default text editor" launches.
    /// </summary>
    public static string TextEditorProgram =>
        OperatingSystem.IsWindows() ? "notepad.exe" : "xdg-open";

    public bool OpenWithDefaultProgram(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool OpenWithProgram(string programPath, string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = programPath,
                Arguments = filePath,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
