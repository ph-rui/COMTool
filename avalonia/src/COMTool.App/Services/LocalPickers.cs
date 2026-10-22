using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace COMTool.App.Services;

/// <summary>
/// The system dialogs of the SFTP panel: the files of an upload and the
/// local directory of a download. The panel asks for them when its
/// toolbar buttons are pressed, so the rest of the panel needs no local
/// listing.
/// </summary>
public interface ILocalPickers
{
    /// <summary>
    /// Asks for the local files that an upload sends. A cancelled dialog
    /// answers with an empty list.
    /// </summary>
    Task<IReadOnlyList<string>> PickFilesAsync(string title);

    /// <summary>
    /// Asks for the local directory that a download writes into. A
    /// cancelled dialog answers with null.
    /// </summary>
    Task<string?> PickFolderAsync(string title);
}

/// <summary>
/// The real dialogs of the desktop: Avalonia hands them out through the
/// top level of the panel.
/// </summary>
public sealed class TopLevelLocalPickers : ILocalPickers
{
    private readonly TopLevel _topLevel;

    public TopLevelLocalPickers(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title)
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        });

        var paths = new List<string>(files.Count);
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

/// <summary>
/// The dialogs of the end-to-end run, which has no desktop that could
/// show them. The answers are read from the environment, so that a run
/// can upload and download without a click into a native dialog:
/// <c>COMTOOL_E2E_PICK_FILES</c> holds the files of an upload and
/// <c>COMTOOL_E2E_PICK_FOLDER</c> holds the directory of a download, a
/// vertical bar separates several files.
/// </summary>
public sealed class ScriptedLocalPickers : ILocalPickers
{
    public const string FilesVariable = "COMTOOL_E2E_PICK_FILES";
    public const string FolderVariable = "COMTOOL_E2E_PICK_FOLDER";
    private const char Separator = '|';

    /// <summary>
    /// True when the run put the answers into the environment.
    /// </summary>
    public static bool IsAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FilesVariable))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FolderVariable));

    public Task<IReadOnlyList<string>> PickFilesAsync(string title)
    {
        var value = Environment.GetEnvironmentVariable(FilesVariable);
        var paths = string.IsNullOrEmpty(value)
            ? Array.Empty<string>()
            : value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Task.FromResult<IReadOnlyList<string>>(paths);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        var value = Environment.GetEnvironmentVariable(FolderVariable);
        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }
}

/// <summary>
/// Chooses the dialogs of a panel: a scripted run answers from the
/// environment, everything else asks the desktop.
/// </summary>
public static class LocalPickers
{
    public static ILocalPickers? Create(Control owner)
    {
        if (ScriptedLocalPickers.IsAvailable)
        {
            return new ScriptedLocalPickers();
        }

        var topLevel = TopLevel.GetTopLevel(owner);
        return topLevel is null ? null : new TopLevelLocalPickers(topLevel);
    }
}
