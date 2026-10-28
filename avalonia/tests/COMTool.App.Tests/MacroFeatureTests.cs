using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using COMTool.Core.Macros;
using COMTool.Sftp;
using Iciclecreek.Terminal;
using Xunit;

namespace COMTool.App.Tests;

public sealed class MacroFeatureTests
{
    [AvaloniaFact]
    public void MAC01_SidebarMacrosPanelMatchesMobaXterm()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Macros;
                RunJobs(window);

                // Record button present with correct initial label.
                var recordBtn = sidebar.FindControl<Button>("MacroRecordButton")!;
                Assert.NotNull(recordBtn);
                // No terminal tab → disabled
                Assert.False(recordBtn.IsEnabled);

                // "Saved macros" header present (empty variant is visible)
                Assert.NotNull(sidebar.FindControl<TextBlock>("SavedMacrosHeaderEmpty"));

                // Old inline editor elements must be gone
                Assert.Null(sidebar.FindControl<TextBox>("MacroNameBox"));
                Assert.Null(sidebar.FindControl<TextBox>("MacroTextBox"));
                Assert.Null(sidebar.FindControl<Button>("SidebarAddMacroButton"));

                // Now add a macro and verify the context menu has 9 items
                vm.Macros.Add(new MacroItem("id1", "TestMacro", new List<MacroStep>
                {
                    MacroStep.TextStep("echo hi")
                }));
                RunJobs(window);

                var rowBorder = sidebar.GetVisualDescendants()
                    .OfType<Border>()
                    .FirstOrDefault(b => b.Name == "MacroRowBorder");
                Assert.NotNull(rowBorder);

                var menu = rowBorder!.ContextMenu;
                Assert.NotNull(menu);

                var menuItems = menu!.Items.OfType<MenuItem>().ToList();
                Assert.Equal(9, menuItems.Count);
                Assert.Equal(2, menu.Items.OfType<Separator>().Count());

                // The six bound headers resolve from MacroItem properties
                // (the ContextMenu DataContext is injected when the menu opens,
                // which headless does not do here).
                var item = vm.Macros[0];
                Assert.Equal("Rename \"TestMacro\"", item.MenuRenameHeader);
                Assert.Equal("Edit \"TestMacro\"", item.MenuEditHeader);
                Assert.Equal("Duplicate \"TestMacro\"", item.MenuDuplicateHeader);
                Assert.Equal("Delete \"TestMacro\"", item.MenuDeleteHeader);
                Assert.Equal("Assign hotkey to \"TestMacro\"", item.MenuHotkeyHeader);
                Assert.Equal("Execute \"TestMacro\"", item.MenuExecuteHeader);

                var desktopShortcut = menuItems.FirstOrDefault(mi =>
                    (string)mi.Header! == "创建桌面快捷方式(W)");
                Assert.NotNull(desktopShortcut);
                Assert.False(desktopShortcut!.IsEnabled);

                Assert.Contains(menuItems, mi => (string)mi.Header! == "导入宏(Y)");
                Assert.Contains(menuItems, mi => (string)mi.Header! == "导出宏(Z)");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC01_LegacyConfigMigratesOnLoad()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "legacy1",
                    Name = "重启",
                    Text = "reboot now",
                    AppendEnter = true,
                });
                env.SeedConfig(config);

                window = CreateWindow(env);
                var vm = Vm(window);

                // After load, the macro should be migrated to step-based form
                Assert.Single(vm.Macros);
                var item = vm.Macros[0];
                Assert.Equal("重启", item.Name);
                Assert.Equal(2, item.Steps.Count);
                Assert.Equal(MacroStepKind.Text, item.Steps[0].Kind);
                Assert.Equal("reboot now", item.Steps[0].Text);
                Assert.Equal(MacroStepKind.Key, item.Steps[1].Kind);
                Assert.Equal("RETURN", item.Steps[1].KeyName);

                // Persist the migration and verify on-disk format
                window.PersistMacros();
                var disk = ReadConfig(env);
                Assert.Single(disk.Macros);
                Assert.Equal(2, disk.Macros[0].Steps.Count);
                Assert.Equal(3, disk.Version);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC01_ExecuteMacroSendsBytes()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                // Add a macro: "ls -la" + RETURN
                vm.Macros.Add(new MacroItem("m1", "ls", new List<MacroStep>
                {
                    MacroStep.TextStep("ls -la"),
                    MacroStep.KeyStep("RETURN"),
                }));

                await vm.ExecuteMacroAsync(vm.Macros[0]);
                RunJobs(window);

                Assert.Equal("ls -la\r", pty.WriterText);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC04_EmptyRecordingDiscarded()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);

                // Start recording
                Assert.True(vm.StartRecording());
                RunJobs(window);
                Assert.True(vm.IsRecording);

                // Stop with no input
                var steps = vm.StopRecording();
                RunJobs(window);

                Assert.Null(steps);
                Assert.False(vm.IsRecording);
                Assert.Empty(vm.Macros);
                // No dialog should appear for empty recording
                Assert.Null(window.LastMacroNameDialogForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC05_StopRecordingWithName_SavesWithDerivedDefault()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                var pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                // Start recording via the sidebar button event
                sidebar.RaiseMacroRecordToggleForTests();
                RunJobs(window);
                Assert.True(vm.IsRecording);
                Assert.NotNull(vm.ActiveRecording);

                // Feed text and a key
                vm.ActiveRecording!.FeedText("echo hi");
                vm.ActiveRecording.FeedKey("RETURN");

                // Stop recording via the sidebar button event
                sidebar.RaiseMacroRecordToggleForTests();
                RunJobs(window);
                Assert.False(vm.IsRecording);

                // Name dialog should appear with default name "echo hi"
                var dialog = window.LastMacroNameDialogForTests;
                Assert.NotNull(dialog);

                // Code-built dialogs have no XAML name scope; find by descendants.
                var nameBox = dialog!.GetVisualDescendants().OfType<TextBox>().First();
                Assert.Equal("echo hi", nameBox.Text);

                // Click OK to accept
                var okBtn = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(b => string.Equals(b.Content?.ToString(), "确定", StringComparison.Ordinal));
                okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, okBtn));
                RunJobs(window);

                // Macro should now be in the list
                Assert.Single(vm.Macros);
                Assert.Equal("echo hi", vm.Macros[0].Name);
                Assert.Equal(2, vm.Macros[0].Steps.Count);
                Assert.Equal(MacroStepKind.Text, vm.Macros[0].Steps[0].Kind);
                Assert.Equal("echo hi", vm.Macros[0].Steps[0].Text);
                Assert.Equal(MacroStepKind.Key, vm.Macros[0].Steps[1].Kind);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC13_RecordingCapturesKeysAndForwardsToPty()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                // Start recording
                Assert.True(vm.StartRecording());
                var rec = vm.ActiveRecording!;
                Assert.Same(terminal, rec.Target);

                // Simulate what the tunnel handlers would do
                rec.FeedText("echo hello");
                rec.FeedKey("RETURN");

                // The terminal should still be independently usable (no key swallowing)
                _ = terminal.SendInputAsync("forwarded");
                WaitUntil(() => pty!.WriterText.Contains("forwarded"), "forwarded bytes");

                // Finish and verify steps
                var steps = vm.StopRecording();
                Assert.NotNull(steps);
                Assert.Equal(2, steps!.Count);
                Assert.Equal(MacroStepKind.Text, steps[0].Kind);
                Assert.Equal("echo hello", steps[0].Text);
                Assert.Equal(MacroStepKind.Key, steps[1].Kind);
                Assert.Equal("RETURN", steps[1].KeyName);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC13b_ExecutionStopsCleanlyWhenTabClosed()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                var tab = TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal);
                vm.AddTab(tab);
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                // Add a macro with a long sleep so it's still running when we close
                vm.Macros.Add(new MacroItem("m1", "SlowMacro", new List<MacroStep>
                {
                    MacroStep.SleepStep(3000),
                    MacroStep.TextStep("never-sent"),
                }));

                // Start execution (fire and forget)
                _ = vm.ExecuteMacroAsync(vm.Macros[0]);
                // Give it a moment to enter the sleep
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.IsExecutingMacro);

                // Close the tab (simulates user closing terminal while macro runs)
                window.CloseTabForTests(tab);
                RunJobs(window);

                // Execution should be cancelled and state reset
                WaitUntil(() => !vm.IsExecutingMacro, "macro execution to stop");
                Assert.False(vm.IsExecutingMacro);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC14_WaitStepTimesOutContinues()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                // Macro: wait for "zzz-nope" (100ms timeout), then send "after"
                vm.Macros.Add(new MacroItem("m1", "WaitTest", new List<MacroStep>
                {
                    MacroStep.WaitStep("zzz-nope", 100),
                    MacroStep.TextStep("after"),
                }));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                await vm.ExecuteMacroAsync(vm.Macros[0]);
                sw.Stop();
                RunJobs(window);

                // Should complete (timeout skips the wait, doesn't abort)
                Assert.False(vm.IsExecutingMacro);
                Assert.Equal("after", pty.WriterText);
                Assert.True(sw.ElapsedMilliseconds < 5000, "wait must not hang");
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC15_DeleteAsksConfirmation()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "m1",
                    Name = "DeleteMe",
                    Steps = new List<MacroStepDto>
                    {
                        new() { Kind = "text", Text = "echo test" },
                    },
                });
                env.SeedConfig(config);

                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                RunJobs(window);

                Assert.Single(vm.Macros);
                var item = vm.Macros[0];
                Assert.Equal("DeleteMe", item.Name);

                // Test: reject delete (click 否 in the confirm dialog)
                sidebar.RaiseMacroDeleteForTests(item);
                WaitUntil(() => window.LastMacroDeleteDialogForTests is not null, "delete dialog");
                var dlg = window.LastMacroDeleteDialogForTests;
                dlg.GetVisualDescendants().OfType<Button>()
                    .First(b => string.Equals(b.Content?.ToString(), "否", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dlg));
                RunJobs(window);
                Assert.Single(vm.Macros); // rejected: still there

                // Test: confirm delete (click 是)
                sidebar.RaiseMacroDeleteForTests(item);
                WaitUntil(() => window.LastMacroDeleteDialogForTests is not null, "delete dialog 2");
                dlg = window.LastMacroDeleteDialogForTests;
                dlg.GetVisualDescendants().OfType<Button>()
                    .First(b => string.Equals(b.Content?.ToString(), "是", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dlg));
                RunJobs(window);

                Assert.Empty(vm.Macros);
                var disk = ReadConfig(env);
                Assert.Empty(disk.Macros);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC06_EditorOpensWithSteps()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "probe",
                    Name = "Probe",
                    Steps = new List<MacroStepDto>
                    {
                        new() { Kind = "text", Text = "echo probe-macro" },
                        new() { Kind = "key", Key = "RETURN" },
                        new() { Kind = "sleep", Milliseconds = 1200 },
                    },
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var item = vm.Macros[0];

                var editor = OpenEditor(window, item);
                try
                {
                    Assert.Equal(3, editor.StepsForTests.Count);
                    Assert.Equal("\"echo probe-macro\"", editor.StepsForTests[0].DisplaySummary);
                    Assert.Equal("RETURN", editor.StepsForTests[1].DisplaySummary);
                    Assert.Equal("SLEEP ms=1200", editor.StepsForTests[2].DisplaySummary);
                    Assert.NotNull(editor.FindControl<ListBox>("StepList"));
                    Assert.NotNull(editor.FindControl<Button>("EditStepButton"));
                    Assert.NotNull(editor.FindControl<Button>("InsertAboveButton"));
                    Assert.NotNull(editor.FindControl<Button>("InsertBelowButton"));
                    Assert.NotNull(editor.FindControl<Button>("DeleteStepButton"));
                    Assert.NotNull(editor.FindControl<ComboBox>("HotkeyCombo"));
                }
                finally
                {
                    RaiseClick(editor.FindControl<Button>("CancelButton")!);
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }

                // 取消后宏不变
                Assert.Equal(3, vm.Macros[0].Steps.Count);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC06b_EditorHandlesLargeStepList()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var steps = Enumerable.Range(0, 1001)
                    .Select(i => new MacroStepDto { Kind = "text", Text = $"step{i}" })
                    .ToList();
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig { Id = "big", Name = "Big", Steps = steps });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var item = vm.Macros[0];

                var editor = OpenEditor(window, item);
                try
                {
                    Assert.Equal(1001, editor.StepsForTests.Count);
                    Assert.NotNull(editor.FindControl<ListBox>("StepList"));
                }
                finally
                {
                    RaiseClick(editor.FindControl<Button>("OkButton")!);
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }

                RunJobs(window);
                Assert.Equal(1001, vm.Macros[0].Steps.Count);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC07_StepEditorShowsFourTypes()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "m1",
                    Name = "M",
                    Steps = new List<MacroStepDto> { new() { Kind = "text", Text = "hi" } },
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var item = vm.Macros[0];

                var editor = OpenEditor(window, item);
                try
                {
                    editor.FindControl<ListBox>("StepList")!.SelectedIndex = 0;
                    RunJobs(window);
                    RaiseClick(editor.FindControl<Button>("EditStepButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is not null, "step dialog");
                    var dlg = editor.LastMacroStepDialogForTests!;

                    // 4 radio 类型齐全
                    Assert.NotNull(dlg.FindControl<RadioButton>("RadioText"));
                    Assert.NotNull(dlg.FindControl<RadioButton>("RadioKey"));
                    Assert.NotNull(dlg.FindControl<RadioButton>("RadioSleep"));
                    Assert.NotNull(dlg.FindControl<RadioButton>("RadioWait"));

                    // 默认文本：延迟字段可见
                    Assert.True(dlg.FindControl<StackPanel>("TextPanel")!.IsVisible);
                    Assert.Equal("按键之间的延迟:",
                        dlg.FindControl<TextBlock>("TextDelayLabel")!.Text);
                    Assert.False(dlg.FindControl<StackPanel>("SleepPanel")!.IsVisible);
                    Assert.False(dlg.FindControl<StackPanel>("KeyPanel")!.IsVisible);
                    Assert.Equal(26, dlg.FindControl<ComboBox>("KeyValue")!.Items.Count);

                    // 切到睡眠：ms 字段出现
                    dlg.FindControl<RadioButton>("RadioSleep")!.IsChecked = true;
                    RunJobs(window);
                    Assert.True(dlg.FindControl<StackPanel>("SleepPanel")!.IsVisible);
                    Assert.False(dlg.FindControl<StackPanel>("TextPanel")!.IsVisible);

                    // 切到按键
                    dlg.FindControl<RadioButton>("RadioKey")!.IsChecked = true;
                    RunJobs(window);
                    Assert.True(dlg.FindControl<StackPanel>("KeyPanel")!.IsVisible);

                    RaiseClick(dlg.FindControl<Button>("CancelButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is null, "step dialog closed");
                }
                finally
                {
                    RaiseClick(editor.FindControl<Button>("CancelButton")!);
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC07b_HotkeyConflictRejected()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "a", Name = "A",
                    Steps = new List<MacroStepDto> { new() { Kind = "text", Text = "a" } },
                });
                config.Macros.Add(new MacroConfig
                {
                    Id = "b", Name = "B",
                    Steps = new List<MacroStepDto> { new() { Kind = "text", Text = "b" } },
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var a = vm.Macros[0];
                var b = vm.Macros[1];

                Assert.True(vm.AssignHotkey(a, "Ctrl+Shift+A"));

                // 打开 B 的编辑器，抢 A 的热键
                var editor = OpenEditor(window, b);
                try
                {
                    editor.FindControl<ComboBox>("HotkeyCombo")!.SelectedItem = "Ctrl+Shift+A";
                    RaiseClick(editor.FindControl<Button>("OkButton")!);
                }
                finally
                {
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }

                RunJobs(window);
                Assert.Null(b.Hotkey);              // 冲突被拒
                Assert.Equal("Ctrl+Shift+A", a.Hotkey); // A 保留

                // 错误提示出现
                WaitUntil(() => window.LastErrorDialogForTests is not null, "error dialog");
                var err = window.LastErrorDialogForTests!;
                Assert.True(err.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text != null && t.Text.Contains("快捷键不可用")));
                var errOk = err.GetVisualDescendants().OfType<Button>()
                    .First(bt => bt.Content is string s && s == "OK");
                RaiseClick(errOk);
                WaitUntil(() => window.LastErrorDialogForTests is null, "error dialog closed");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC08_EditorInsertDeleteSteps()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "m1",
                    Name = "M",
                    Steps = new List<MacroStepDto>
                    {
                        new() { Kind = "text", Text = "x" },
                        new() { Kind = "text", Text = "y" },
                    },
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var item = vm.Macros[0];

                var editor = OpenEditor(window, item);
                try
                {
                    Assert.Equal(2, editor.StepsForTests.Count);

                    // 在第 0 行上方插入文本 "a"
                    editor.FindControl<ListBox>("StepList")!.SelectedIndex = 0;
                    RunJobs(window);
                    RaiseClick(editor.FindControl<Button>("InsertAboveButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is not null, "step dialog");
                    var dlg = editor.LastMacroStepDialogForTests!;
                    dlg.FindControl<TextBox>("TextValue")!.Text = "a";
                    RaiseClick(dlg.FindControl<Button>("OkButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is null, "step dialog closed");
                    RunJobs(window);

                    Assert.Equal(3, editor.StepsForTests.Count);
                    Assert.Equal("a", editor.StepsForTests[0].Text);

                    // 删除选中的第 0 行（"a"）
                    RaiseClick(editor.FindControl<Button>("DeleteStepButton")!);
                    RunJobs(window);
                    Assert.Equal(2, editor.StepsForTests.Count);

                    // 确定回写
                    RaiseClick(editor.FindControl<Button>("OkButton")!);
                }
                finally
                {
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }

                RunJobs(window);
                Assert.Equal(2, vm.Macros[0].Steps.Count);
                Assert.Equal("x", vm.Macros[0].Steps[0].Text);
                Assert.Equal("y", vm.Macros[0].Steps[1].Text);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC08b_EditorCancelDiscards()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "m1",
                    Name = "M",
                    Steps = new List<MacroStepDto> { new() { Kind = "text", Text = "orig" } },
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                var item = vm.Macros[0];

                var editor = OpenEditor(window, item);
                try
                {
                    editor.FindControl<ListBox>("StepList")!.SelectedIndex = 0;
                    RunJobs(window);
                    RaiseClick(editor.FindControl<Button>("InsertAboveButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is not null, "step dialog");
                    var dlg = editor.LastMacroStepDialogForTests!;
                    dlg.FindControl<TextBox>("TextValue")!.Text = "new";
                    RaiseClick(dlg.FindControl<Button>("OkButton")!);
                    WaitUntil(() => editor.LastMacroStepDialogForTests is null, "step dialog closed");
                    RunJobs(window);

                    Assert.Equal(2, editor.StepsForTests.Count); // 工作副本已变

                    // 取消编辑器 → 全部丢弃
                    RaiseClick(editor.FindControl<Button>("CancelButton")!);
                }
                finally
                {
                    WaitUntil(() => window.LastMacroEditorWindowForTests is null, "editor closed");
                }

                RunJobs(window);
                Assert.Equal(1, vm.Macros[0].Steps.Count);
                Assert.Equal("orig", vm.Macros[0].Steps[0].Text);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC16_BannerShowsWhileRecording()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);

                var banner = window.FindControl<Border>("MacroStatusBanner")!;
                Assert.False(banner.IsVisible);

                // 录制中: 横幅出现, 文案含 MobaXterm 原文
                sidebar.RaiseMacroRecordToggleForTests();
                RunJobs(window);
                Assert.True(banner.IsVisible);
                Assert.True(banner.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text != null && t.Text.Contains("正在录制新宏")));

                // 停止(空录制): 横幅隐藏
                sidebar.RaiseMacroRecordToggleForTests();
                RunJobs(window);
                Assert.False(vm.IsRecording);
                Assert.False(banner.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC17_ExecutionBannerAndCancel()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                vm.Macros.Add(new MacroItem("m1", "Slow", new List<MacroStep>
                {
                    MacroStep.SleepStep(3000),
                    MacroStep.TextStep("never"),
                }));

                _ = vm.ExecuteMacroAsync(vm.Macros[0]);
                await Task.Delay(50);
                RunJobs(window);

                var banner = window.FindControl<Border>("MacroStatusBanner")!;
                Assert.True(banner.IsVisible);
                Assert.True(banner.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text != null && t.Text.Contains("正在执行宏")));

                // 点横幅"取消" → 执行终止, 状态复位, 后续步骤未发送
                RaiseClick(window.FindControl<Button>("MacroBannerActionButton")!);
                WaitUntil(() => !vm.IsExecutingMacro, "execution cancelled");
                RunJobs(window);
                Assert.False(banner.IsVisible);
                Assert.False(pty!.WriterText.Contains("never"));
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC11_HotkeyAssignAndTrigger()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                var item = new MacroItem("m1", "Hk", new List<MacroStep>
                {
                    MacroStep.TextStep("hot"),
                    MacroStep.KeyStep("RETURN"),
                });
                vm.Macros.Add(item);

                // 保留组合
                Assert.True(MacroHotkey.IsReserved(Key.Space, KeyModifiers.Control));
                Assert.True(MacroHotkey.IsReserved(Key.F11, KeyModifiers.None));
                Assert.True(MacroHotkey.IsReserved(Key.W, KeyModifiers.Control));

                // 分配正常, 重复分配被拒
                Assert.True(vm.AssignHotkey(item, "Ctrl+Shift+M"));
                Assert.Equal("Ctrl+Shift+M", item.Hotkey);
                var other = new MacroItem("m2", "Other",
                    new List<MacroStep> { MacroStep.TextStep("x") });
                vm.Macros.Add(other);
                Assert.False(vm.AssignHotkey(other, "Ctrl+Shift+M"));
                Assert.Null(other.Hotkey);

                // 触发: 在终端子树内按下 Ctrl+Shift+M → 宏字节到达 pty
                PressKey(terminal, Key.M, KeyModifiers.Control | KeyModifiers.Shift);
                WaitUntil(() => pty!.WriterText == "hot\r", "hotkey macro bytes");
                Assert.Equal("hot\r", pty.WriterText);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MAC12_CtrlSpacePopupListsAndExecutes()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);

                vm.Macros.Add(new MacroItem("m1", "First", new List<MacroStep>
                {
                    MacroStep.TextStep("one"),
                    MacroStep.KeyStep("RETURN"),
                }));
                vm.Macros.Add(new MacroItem("m2", "Second", new List<MacroStep>
                {
                    MacroStep.TextStep("two"),
                    MacroStep.KeyStep("RETURN"),
                }));
                RunJobs(window);

                var popup = window.FindControl<Border>("MacroPopup")!;
                Assert.False(popup.IsVisible);

                // Ctrl+Space 打开
                PressKey(terminal, Key.Space, KeyModifiers.Control);
                RunJobs(window);
                Assert.True(popup.IsVisible);

                // Esc 关闭, 不执行
                PressKey(terminal, Key.Escape, KeyModifiers.None);
                RunJobs(window);
                Assert.False(popup.IsVisible);
                Assert.Equal(string.Empty, pty!.WriterText);

                // 再打开, ArrowDown 到第二个, Enter 执行
                PressKey(terminal, Key.Space, KeyModifiers.Control);
                RunJobs(window);
                Assert.True(popup.IsVisible);
                PressKey(terminal, Key.Down, KeyModifiers.None);
                RunJobs(window);
                PressKey(terminal, Key.Enter, KeyModifiers.None);
                WaitUntil(() => pty!.WriterText == "two\r", "popup macro bytes");
                RunJobs(window);
                Assert.False(popup.IsVisible);
                Assert.Equal("two\r", pty.WriterText);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // --- Helpers (mirrored from SidebarRailTests) ---

    /// <summary>Opens the macro step editor via the sidebar event and waits for it.</summary>
    private static MacroEditorWindow OpenEditor(MainWindow window, MacroItem item)
    {
        Sidebar(window).RaiseMacroEditForTests(item);
        WaitUntil(() => window.LastMacroEditorWindowForTests is not null, "macro editor window");
        return (MacroEditorWindow)window.LastMacroEditorWindowForTests!;
    }

    /// <summary>Fires a button's Click handler without geometry (headless-safe).</summary>
    private static void RaiseClick(Control control) =>
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, control));

    /// <summary>Raised a raw key-down on a control (tunnel reaches MainWindow).</summary>
    private static void PressKey(Control target, Key key, KeyModifiers mods)
    {
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = mods,
        });
    }

    private static MainWindow CreateWindow(TestEnvironment env)
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        window.StopPortTimerForTests();
        RunJobs(window);
        return window;
    }

    private static MainViewModel Vm(MainWindow window) =>
        (MainViewModel)window.DataContext!;

    private static SessionSidebar Sidebar(MainWindow window) =>
        window.FindControl<SessionSidebar>("Sidebar")!;

    private static ComtoolConfig ReadConfig(TestEnvironment env) =>
        ComtoolConfig.Load(System.IO.Path.Combine(env.AvaloniaConfigDir, "config.json"));

    private static void RunJobs(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
