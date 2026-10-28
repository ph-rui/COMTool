using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace COMTool.App.Services;

public interface IMacroFilePickers
{
    Task<string?> PickExportPathAsync(string title, string defaultFileName);
    Task<string?> PickImportPathAsync(string title);
}

public sealed class TopLevelMacroFilePickers : IMacroFilePickers
{
    private readonly TopLevel _topLevel;

    public TopLevelMacroFilePickers(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> PickExportPathAsync(string title, string defaultFileName)
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = "mxtmacros",
        });
        return file?.Path.LocalPath;
    }

    public async Task<string?> PickImportPathAsync(string title)
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}

public sealed class ScriptedMacroFilePickers : IMacroFilePickers
{
    public const string ExportPathVariable = "COMTOOL_E2E_MACRO_EXPORT_PATH";
    public const string ImportPathVariable = "COMTOOL_E2E_MACRO_IMPORT_PATH";

    public static bool IsAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ExportPathVariable))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ImportPathVariable));

    public Task<string?> PickExportPathAsync(string title, string defaultFileName)
    {
        var value = Environment.GetEnvironmentVariable(ExportPathVariable);
        return Task.FromResult<string?>(string.IsNullOrEmpty(value) ? null : value);
    }

    public Task<string?> PickImportPathAsync(string title)
    {
        var value = Environment.GetEnvironmentVariable(ImportPathVariable);
        return Task.FromResult<string?>(string.IsNullOrEmpty(value) ? null : value);
    }
}

public static class MacroFilePickers
{
    public static IMacroFilePickers? Create(TopLevel topLevel)
    {
        if (ScriptedMacroFilePickers.IsAvailable)
            return new ScriptedMacroFilePickers();

        return topLevel is null ? null : new TopLevelMacroFilePickers(topLevel);
    }
}
