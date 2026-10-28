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
                terminal.SendInputAsync("forwarded").GetAwaiter().GetResult();
                RunJobs(window);
                Assert.Contains("forwarded", pty.WriterText);

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

    // --- Helpers (mirrored from SidebarRailTests) ---

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
