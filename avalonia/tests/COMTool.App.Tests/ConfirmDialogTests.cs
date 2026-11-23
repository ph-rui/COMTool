using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Views;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r67.9: unified confirmation dialog used by "remove session".
/// The previous dialog was an inline 440x190 Window with native chrome
/// (minimize/maximize), one flat TextBlock for question + label, and two
/// equal gray No/Yes buttons.
/// </summary>
public sealed class ConfirmDialogTests
{
    private static (Window Owner, ConfirmDialog Dialog, Task<bool> Result) Open(
        string name = "192.168.1.12",
        string detail = "root@192.168.1.12:23",
        string? note = null,
        bool danger = true)
    {
        var owner = new Window { Width = 640, Height = 480 };
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var dialog = ConfirmDialog.Create(
            "Remove session",
            "Remove this Telnet session?",
            name,
            detail,
            note,
            confirmText: "Remove",
            danger: danger);
        var result = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (owner, dialog, result);
    }

    private static void Close(Window owner, ConfirmDialog dialog)
    {
        if (dialog.IsVisible)
        {
            dialog.Close(false);
        }

        owner.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogUsesFramelessChrome()
    {
        var (owner, dialog, _) = Open();
        try
        {
            Assert.Equal(WindowDecorations.None, dialog.WindowDecorations);
            Assert.False(dialog.CanResize);
            Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
            Assert.Equal(436, dialog.Width);
            Assert.Equal(new Thickness(1), dialog.FindControl<Border>("DialogBorder")!.BorderThickness);
            Assert.True(dialog.FindControl<Border>("DialogTitleBar")!.Bounds.Height is >= 34.5 and <= 35.5);

            var title = dialog.FindControl<TextBlock>("DialogTitleText")!;
            Assert.Equal("Remove session", title.Text);

            // 自绘标题栏：只有关闭按钮，没有系统的最小化/最大化。
            Assert.NotNull(dialog.FindControl<Button>("DialogCloseButton"));
            Assert.DoesNotContain(
                dialog.GetVisualDescendants().OfType<Button>(),
                b => b.Name is "WindowMinButton" or "WindowMaxButton");
        }
        finally
        {
            Close(owner, dialog);
        }
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogRendersQuestionNameAndEndpoint()
    {
        var (owner, dialog, _) = Open();
        try
        {
            var question = dialog.FindControl<TextBlock>("QuestionText")!;
            var name = dialog.FindControl<TextBlock>("NameText")!;
            var detail = dialog.FindControl<TextBlock>("DetailText")!;
            var note = dialog.FindControl<TextBlock>("NoteText")!;

            Assert.Equal("Remove this Telnet session?", question.Text);
            Assert.Equal(13.0, question.FontSize);
            Assert.Equal(BrushColor("WindowTextBrush"), Solid(question.Foreground));

            Assert.Equal("192.168.1.12", name.Text);
            Assert.Equal(12.0, name.FontSize);
            Assert.Equal(BrushColor("SessionNameBrush"), Solid(name.Foreground));

            Assert.Equal("root@192.168.1.12:23", detail.Text);
            Assert.Equal(BrushColor("SessionDetailBrush"), Solid(detail.Foreground));
            Assert.False(note.IsVisible);

            // 三段自上而下排列，视觉层级由字号与颜色区分。
            Assert.True(name.Bounds.Y > question.Bounds.Y);
            Assert.True(detail.Bounds.Y > name.Bounds.Y);
        }
        finally
        {
            Close(owner, dialog);
        }
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogTrimsLongSessionNames()
    {
        var longName = new string('x', 200);
        var (owner, dialog, _) = Open(name: longName, detail: "root@192.168.1.12:23");
        try
        {
            var name = dialog.FindControl<TextBlock>("NameText")!;
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, name.TextWrapping);
            // 窗口宽度保持 436，长名称在内容区内截断而不是把窗口撑宽。
            Assert.Equal(436, dialog.Bounds.Width);
            Assert.True(name.Bounds.Width <= 404, $"名称行应被截断到内容宽度内，实际 {name.Bounds.Width}");
        }
        finally
        {
            Close(owner, dialog);
        }
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogButtonsAndKeyboard()
    {
        var (owner, dialog, result) = Open(note: "This cannot be undone.");
        try
        {
            var cancel = dialog.FindControl<Button>("CancelButton")!;
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            var note = dialog.FindControl<TextBlock>("NoteText")!;

            Assert.Equal("Remove", confirm.Content as string);
            Assert.Contains("DialogDanger", confirm.Classes);
            Assert.Equal(BrushColor("DangerBrush"), Solid(confirm.Background));
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.False(confirm.IsDefault);

            // 取消在左、确认在右（与 Windows 对话框一致）。
            Assert.True(cancel.Bounds.X < confirm.Bounds.X);

            // 备注行是可选段落，不传时不占位。
            Assert.True(note.IsVisible);
            Assert.Equal("This cannot be undone.", note.Text);

            // Enter 与 Esc 都走取消；确认只能点击或在确认按钮上按空格。
            dialog.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Return,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
        }
        finally
        {
            Close(owner, dialog);
        }
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogEscapeAndConfirmClick()
    {
        var (owner, dialog, result) = Open();
        try
        {
            dialog.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
        }
        finally
        {
            Close(owner, dialog);
        }

        var (owner2, dialog2, result2) = Open();
        try
        {
            dialog2.FindControl<Button>("ConfirmButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(result2.IsCompleted);
            Assert.True(result2.Result);
        }
        finally
        {
            Close(owner2, dialog2);
        }
    }

    [AvaloniaFact]
    public void UI17_ConfirmDialogNonDangerVariantUsesNormalButton()
    {
        var (owner, dialog, _) = Open(danger: false);
        try
        {
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            Assert.DoesNotContain("DialogDanger", confirm.Classes);
            Assert.NotEqual(BrushColor("DangerBrush"), Solid(confirm.Background));
        }
        finally
        {
            Close(owner, dialog);
        }
    }

    private static Color BrushColor(string key)
    {
        Assert.True(
            Application.Current!.TryFindResource(key, out var value),
            $"资源 {key} 未找到");
        return ((ISolidColorBrush)value!).Color;
    }

    private static Color Solid(IBrush? brush) =>
        ((ISolidColorBrush)brush!).Color;
}
