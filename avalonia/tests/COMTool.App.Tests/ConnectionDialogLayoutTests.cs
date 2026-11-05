using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// r49: connection-settings dialog layout fixes (user screenshots):
/// 1. The SSH "Key file" browse button rendered U+2026 "…" on the text baseline,
///    sitting ~8px below the button centre inside a 28px box. MobaXterm shows a
///    vertically centered ellipsis, so the button now uses the FontAwesome
///    ellipsis glyph (f141), designed to sit at the em-box centre.
/// 2. The TCP "Auto reconnect" label (~84px at 12pt) overflowed its 86px label
///    column: the trailing "t" touched the checkbox border (measured in the
///    user screenshot: text right edge x=107, checkbox left edge x=107).
///    All four panels now use a common 108px label column so the longest label
///    fits with a visible gap and the input column stays aligned across types.
/// </summary>
public sealed class ConnectionDialogLayoutTests
{
    private static ConnectionSettingsWindow OpenDialog(TestEnvironment env)
    {
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();
        Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
        var window = new ConnectionSettingsWindow(
            service, null, open, open, open, open, _ => Task.CompletedTask);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void UI06_SshKeyFileBrowseButtonUsesCenteredIconGlyph()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = OpenDialog(env);
            try
            {
                // Named so the real-device self-test and this test can locate it.
                var button = window.FindControl<Button>("KeyFileBrowseButton");
                Assert.NotNull(button);

                // U+2026 sits on the baseline and rendered low in the 28px button;
                // the FontAwesome ellipsis (f141) is designed at the em-box centre.
                Assert.Equal("\uf141", button!.Content as string);

                // Must render with the bundled icon font, not the UI text font.
                // FontFamily 由 axaml 的 avares 字符串构造，ToString 返回其来源
                var source = button.FontFamily.ToString();
                Assert.Contains("fontawesome6-solid", source, StringComparison.OrdinalIgnoreCase);

                // Fixed 30px wide; height stretches to match the textbox at layout time
                // (checked geometrically in UI06_SshKeyFileBrowseButtonVerticallyCenteredWithKeyFileBox,
                // because the Fluent theme's TextBox MinHeight (32) overrides any explicit Height).
                Assert.Equal(30.0, button.Width);
                Assert.True(double.IsNaN(button.Height));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public void UI06_SshKeyFileBrowseButtonVerticallyCenteredWithKeyFileBox()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = OpenDialog(env);
            try
            {
                // Switch to the SSH panel (hidden panels are not laid out).
                var combo = window.FindControl<ComboBox>("ConnectionTypeCombo")!;
                combo.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var button = window.FindControl<Button>("KeyFileBrowseButton")!;
                var keyBox = window.FindControl<TextBox>("KeyFileBox")!;

                // Both boxes must be measured; the button stretches to the row whose
                // height is set by the textbox (Fluent TextBox MinHeight 32 beats the
                // Height="28" attribute), so both must end up exactly the same height.
                Assert.True(button.Bounds.Height > 0);
                Assert.True(keyBox.Bounds.Height > 0);
                Assert.Equal(keyBox.Bounds.Height, button.Bounds.Height);

                // Same vertical centre: the ellipsis must sit centred in the row, not
                // low on the baseline as the old U+2026 "..." text did.
                var buttonCentre = button.Bounds.Y + button.Bounds.Height / 2;
                var boxCentre = keyBox.Bounds.Y + keyBox.Bounds.Height / 2;
                Assert.True(Math.Abs(buttonCentre - boxCentre) <= 1.0);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public void UI06_ConnectionDialogLabelColumnFitsLongestLabel()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = OpenDialog(env);
            try
            {
                // "Auto reconnect" is the longest label (~84px at 12pt). The old 86px
                // column left no gap and the trailing "t" collided with the checkbox
                // (user screenshot: text ends x=107, checkbox starts x=107).
                // Headless font metrics differ from Windows, so assert the column
                // width contract: >= 100px, and identical across all four panels so
                // the input column stays aligned when switching connection types.
                var panels = new[] { "SerialPanel", "SshPanel", "TelnetPanel", "TcpPanel" };
                var widths = panels
                    .Select(name => window.FindControl<Grid>(name)!.ColumnDefinitions[0].Width)
                    .ToArray();

                foreach (var width in widths)
                {
                    Assert.False(width.IsStar);
                    Assert.True(width.Value >= 100);
                }
                Assert.Equal(widths[0].Value, widths[1].Value);
                Assert.Equal(widths[0].Value, widths[2].Value);
                Assert.Equal(widths[0].Value, widths[3].Value);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
