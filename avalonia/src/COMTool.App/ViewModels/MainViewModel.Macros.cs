using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using COMTool.App.Services;
using COMTool.Core.Macros;
using Iciclecreek.Terminal;

namespace COMTool.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private MacroRecordingSession? _activeRecording;

    [ObservableProperty]
    private bool _isExecutingMacro;

    [ObservableProperty]
    private string _executingMacroName = "";

    private CancellationTokenSource? _executionCts;
    private TerminalControl? _executingTerminal;

    public bool IsRecording => ActiveRecording is not null;

    public bool CanExecuteMacro =>
        SelectedTab?.Terminal is not null && !IsExecutingMacro && ActiveRecording is null;

    /// <summary>
    /// The sidebar/title-bar record toggle must stay clickable while
    /// recording (to stop it), and needs a live terminal to start.
    /// </summary>
    public bool CanToggleRecording =>
        SelectedTab?.Terminal is not null && (IsRecording || !IsExecutingMacro);

    /// <summary>录制开关按钮文案（汉化）。</summary>
    public string RecordButtonText =>
        IsRecording ? "停止录制宏" : "录制新宏";

    partial void OnActiveRecordingChanged(MacroRecordingSession? value)
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanExecuteMacro));
        OnPropertyChanged(nameof(CanToggleRecording));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    partial void OnIsExecutingMacroChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExecuteMacro));
        OnPropertyChanged(nameof(CanToggleRecording));
    }

    /// <summary>
    /// Starts recording on the active tab's terminal. Returns false when
    /// there is no active terminal, or recording/execution is in progress.
    /// </summary>
    public bool StartRecording()
    {
        if (IsRecording || IsExecutingMacro || SelectedTab?.Terminal is not TerminalControl terminal)
        {
            return false;
        }

        ActiveRecording = new MacroRecordingSession(terminal);
        return true;
    }

    /// <summary>
    /// Stops recording. Returns the captured steps, or null when nothing
    /// was recorded (MobaXterm discards empty recordings silently).
    /// </summary>
    public IReadOnlyList<MacroStep>? StopRecording()
    {
        if (ActiveRecording is not { } session)
        {
            return null;
        }

        var steps = session.Finish();
        ActiveRecording = null;
        return steps;
    }

    /// <summary>
    /// Plays the macro into the active terminal. The target is captured at
    /// start, so switching tabs mid-run keeps sending to the original one.
    /// </summary>
    public async Task ExecuteMacroAsync(MacroItem? item)
    {
        if (item is null || IsExecutingMacro || IsRecording)
        {
            return;
        }

        if (SelectedTab?.Terminal is not { } terminal)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _executionCts = cts;
        _executingTerminal = terminal;
        IsExecutingMacro = true;
        ExecutingMacroName = item.Name;
        try
        {
            var def = new MacroDefinition(item.Name, item.Steps, item.Hotkey);
            var sink = new TerminalMacroSink(terminal);
            await MacroRunner.RunAsync(def, sink, cts.Token);
        }
        catch (Exception)
        {
            // A dead terminal mid-run must not take the app down.
        }
        finally
        {
            _executionCts?.Dispose();
            _executionCts = null;
            _executingTerminal = null;
            IsExecutingMacro = false;
            ExecutingMacroName = "";
        }
    }

    public void CancelMacroExecution() => _executionCts?.Cancel();

    /// <summary>
    /// Cancels the running execution when its target terminal goes away
    /// (the tab is closed). Switching tabs does not cancel: playback keeps
    /// the original terminal reference, MobaXterm-style.
    /// </summary>
    public void CancelIfExecutingOn(TerminalControl? terminal)
    {
        if (terminal is not null && ReferenceEquals(_executingTerminal, terminal))
        {
            _executionCts?.Cancel();
        }
    }

    /// <summary>
    /// Appends a newly recorded/imported macro. Returns false on an empty
    /// name or an empty step list.
    /// </summary>
    public bool SaveNewMacro(string name, IReadOnlyList<MacroStep> steps, string? hotkey = null)
    {
        if (string.IsNullOrWhiteSpace(name) || steps is null || steps.Count == 0)
        {
            return false;
        }

        Macros.Add(new MacroItem(Guid.NewGuid().ToString("N"), name, steps, hotkey));
        return true;
    }

    public void RemoveMacro(MacroItem item) => Macros.Remove(item);

    public MacroItem? DuplicateMacro(MacroItem item)
    {
        var copy = new MacroItem(
            Guid.NewGuid().ToString("N"), item.Name + " (copy)", item.Steps, null);
        Macros.Add(copy);
        return copy;
    }

    public void RenameMacro(MacroItem item, string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            item.Name = newName;
        }
    }

    /// <summary>
    /// Assigns a hotkey. Rejects reserved combos and duplicates (other
    /// macros keep their hotkey). Returns true when accepted.
    /// </summary>
    public bool AssignHotkey(MacroItem item, string? hotkey)
    {
        if (hotkey is null || hotkey == "<none>")
        {
            item.Hotkey = null;
            return true;
        }

        if (!MacroHotkey.TryParse(hotkey, out var key, out var mods))
        {
            return false;
        }

        if (MacroHotkey.IsReserved(key, mods) || MacroHotkey.IsTaken(Macros, hotkey, item.Id))
        {
            return false;
        }

        item.Hotkey = hotkey;
        return true;
    }

    /// <summary>
    /// Tries to match the pressed key against every macro hotkey.
    /// </summary>
    public bool TryMatchHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods, out MacroItem? macro)
    {
        var actual = mods & (Avalonia.Input.KeyModifiers.Control
            | Avalonia.Input.KeyModifiers.Shift
            | Avalonia.Input.KeyModifiers.Alt);
        macro = null;
        foreach (var m in Macros)
        {
            if (string.IsNullOrEmpty(m.Hotkey))
            {
                continue;
            }

            if (MacroHotkey.TryParse(m.Hotkey, out var hk, out var hm)
                && hk == key && hm == actual)
            {
                macro = m;
                return true;
            }
        }

        return false;
    }

    public string ExportMacrosText()
    {
        var defs = Macros
            .Select(m => new MacroDefinition(m.Name, m.Steps, m.Hotkey))
            .ToList();
        return MacroFile.Export(defs);
    }

    /// <summary>
    /// Imports macros from .mxtmacros text. Name clashes get an " (imported)"
    /// suffix until unique. Returns the imported count and parse warnings.
    /// </summary>
    public (int Imported, IReadOnlyList<string> Warnings) ImportMacrosText(string content)
    {
        var (defs, warnings) = MacroFile.Parse(content);
        var imported = 0;
        foreach (var def in defs)
        {
            var name = def.Name;
            while (Macros.Any(m => m.Name == name))
            {
                name += " (imported)";
            }

            var item = new MacroItem(
                Guid.NewGuid().ToString("N"), name, def.Steps, def.Hotkey);
            Macros.Add(item);
            imported++;
        }

        return (imported, warnings);
    }

    /// <summary>
    /// MobaXterm derives the default macro name from the first recorded
    /// line; falls back to the first step's summary.
    /// </summary>
    public static string DefaultRecordedName(IReadOnlyList<MacroStep> steps)
    {
        var firstText = steps
            .FirstOrDefault(s => s.Kind == MacroStepKind.Text && s.Text.Length > 0);
        if (firstText is not null)
        {
            var line = firstText.Text.Split('\n')[0].Trim();
            if (line.Length > 0)
            {
                return line;
            }
        }

        return steps.Count > 0 ? steps[0].DisplaySummary : "Macro";
    }
}
