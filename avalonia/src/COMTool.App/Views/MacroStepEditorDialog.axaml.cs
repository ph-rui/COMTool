using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using COMTool.Core.Macros;

namespace COMTool.App.Views;

/// <summary>
/// Step-type dialog (MobaXterm "Macro 编辑" wording): 文本 / 按键 / 睡眠 /
/// 等待字符串. New steps default to 文本; <see cref="SetStep"/> back-fills
/// when editing an existing step.
/// </summary>
public partial class MacroStepEditorDialog : Window
{
    private MacroStepEditorDialog()
    {
        InitializeComponent();
        foreach (var key in MacroKeys.All)
        {
            KeyValue.Items.Add(key);
        }

        // PropertyChanged (not Click) so programmatic IsChecked changes also
        // update the field visibility.
        RadioText.PropertyChanged += OnTypeChanged;
        RadioKey.PropertyChanged += OnTypeChanged;
        RadioSleep.PropertyChanged += OnTypeChanged;
        RadioWait.PropertyChanged += OnTypeChanged;

        UpdateFieldVisibility();
    }

    public static MacroStepEditorDialog Create() => new();

    /// <summary>The accepted step, or null on cancel.</summary>
    public MacroStep? Result { get; private set; }

    /// <summary>Back-fills an existing step; null = fresh (defaults to 文本).</summary>
    public void SetStep(MacroStep? step)
    {
        switch (step?.Kind)
        {
            case MacroStepKind.Key:
                RadioKey.IsChecked = true;
                KeyValue.SelectedItem = step.KeyName;
                break;
            case MacroStepKind.Sleep:
                RadioSleep.IsChecked = true;
                SleepValue.Value = step.Milliseconds;
                break;
            case MacroStepKind.WaitString:
                RadioWait.IsChecked = true;
                WaitValue.Text = step.Pattern;
                break;
            default:
                RadioText.IsChecked = true;
                TextValue.Text = step?.Text ?? string.Empty;
                TextDelay.Value = step?.KeyDelayMs ?? 0;
                break;
        }

        if (KeyValue.SelectedItem is null && KeyValue.Items.Count > 0)
        {
            KeyValue.SelectedIndex = 0;
        }

        UpdateFieldVisibility();
    }

    private void OnTypeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToggleButton.IsCheckedProperty)
        {
            UpdateFieldVisibility();
        }
    }

    private void UpdateFieldVisibility()
    {
        TextPanel.IsVisible = RadioText.IsChecked == true;
        KeyPanel.IsVisible = RadioKey.IsChecked == true;
        SleepPanel.IsVisible = RadioSleep.IsChecked == true;
        WaitPanel.IsVisible = RadioWait.IsChecked == true;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        MacroStep? step = null;
        if (RadioText.IsChecked == true)
        {
            step = MacroStep.TextStep(TextValue.Text, (int)TextDelay.Value);
        }
        else if (RadioKey.IsChecked == true)
        {
            step = MacroStep.KeyStep(KeyValue.SelectedItem as string ?? "RETURN");
        }
        else if (RadioSleep.IsChecked == true)
        {
            step = MacroStep.SleepStep(Math.Max(0, (int)SleepValue.Value));
        }
        else if (RadioWait.IsChecked == true)
        {
            step = MacroStep.WaitStep(WaitValue.Text);
        }

        if (step is not null)
        {
            Result = step;
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
