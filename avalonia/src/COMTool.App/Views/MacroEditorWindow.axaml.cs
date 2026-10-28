using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Core.Macros;

namespace COMTool.App.Views;

/// <summary>
/// MobaXterm-style "Macro 编辑" window: step list on the left, the four
/// step actions (edit / insert above / insert below / delete) on the right,
/// hotkey combo + Ctrl+Space hint at the bottom. Edits go to a working copy;
/// <see cref="Result"/> is set only on 确定 (cancel -> null, item untouched).
/// </summary>
public partial class MacroEditorWindow : Window
{
    private readonly ObservableCollection<MacroStep> _steps = new();

    private MacroEditorWindow()
    {
        InitializeComponent();
        StepList.ItemsSource = _steps;
    }

    public static MacroEditorWindow Create() => new();

    /// <summary>(name, steps, hotkey) on 确定; null on cancel.</summary>
    internal (string Name, IReadOnlyList<MacroStep> Steps, string? Hotkey)? Result { get; private set; }

    internal IReadOnlyList<MacroStep> StepsForTests => _steps;

    /// <summary>Loads a working copy of the macro's steps + current hotkey.</summary>
    public void LoadFrom(MacroItem item)
    {
        _steps.Clear();
        foreach (var step in item.Steps)
        {
            _steps.Add(step);
        }

        foreach (var option in MacroHotkey.Options())
        {
            HotkeyCombo.Items.Add(option);
        }

        var idx = HotkeyCombo.Items.IndexOf(item.Hotkey ?? "<none>");
        HotkeyCombo.SelectedIndex = idx >= 0 ? idx : 0;

        if (_steps.Count > 0)
        {
            StepList.SelectedIndex = 0;
        }
    }

    private async void OnEditStep(object? sender, RoutedEventArgs e)
    {
        if (StepList.SelectedIndex < 0)
        {
            return;
        }

        var index = StepList.SelectedIndex;
        var step = await OpenStepDialogAsync(_steps[index]);
        if (step is not null)
        {
            _steps[index] = step;
        }
    }

    private async void OnInsertAbove(object? sender, RoutedEventArgs e)
    {
        var idx = StepList.SelectedIndex < 0 ? 0 : StepList.SelectedIndex;
        var step = await OpenStepDialogAsync(null);
        if (step is not null)
        {
            _steps.Insert(idx, step);
            StepList.SelectedIndex = idx;
        }
    }

    private async void OnInsertBelow(object? sender, RoutedEventArgs e)
    {
        var idx = StepList.SelectedIndex < 0 ? _steps.Count : StepList.SelectedIndex + 1;
        var step = await OpenStepDialogAsync(null);
        if (step is not null)
        {
            _steps.Insert(idx, step);
            StepList.SelectedIndex = Math.Min(idx, _steps.Count - 1);
        }
    }

    private void OnDeleteStep(object? sender, RoutedEventArgs e)
    {
        if (StepList.SelectedIndex < 0)
        {
            return;
        }

        var idx = StepList.SelectedIndex;
        _steps.RemoveAt(idx);
        if (_steps.Count > 0)
        {
            StepList.SelectedIndex = Math.Min(idx, _steps.Count - 1);
        }
    }

    private async Task<MacroStep?> OpenStepDialogAsync(MacroStep? existing)
    {
        var dialog = MacroStepEditorDialog.Create();
        LastMacroStepDialogForTests = dialog;
        dialog.SetStep(existing);
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroStepDialogForTests, dialog))
            {
                LastMacroStepDialogForTests = null;
            }
        }

        return dialog.Result;
    }

    internal Window? LastMacroStepDialogForTests { get; private set; }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var name = (DataContext as MacroItem)?.Name ?? string.Empty;
        var hotkey = HotkeyCombo.SelectedItem as string;
        Result = (name, _steps.ToList(),
            hotkey is null || hotkey == "<none>" ? null : hotkey);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
