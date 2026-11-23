using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace COMTool.App.Views;

/// <summary>
/// r67.9：应用内统一的确认对话框。
/// </summary>
/// <remarks>
/// <para>替代 <c>MainWindow.ShowConfirmAsync</c> 里内联拼的 440×190 固定尺寸窗口：
/// 无系统标题栏（<c>WindowDecorations="None"</c>，与主窗一致），自绘 35px 标题栏，
/// 高度随内容自适应；问句、会话名称、端点分三段显示；确认按钮使用危险色。</para>
/// <para>键盘语义：Esc 与 Enter 都触发 Cancel（<c>IsDefault</c> + <c>IsCancel</c>），
/// 破坏性操作只能在按钮上点击或用空格键确认（Tab 把焦点移到确认按钮）。</para>
/// </remarks>
public sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        Opened += (_, _) => CancelButton.Focus();
    }

    /// <summary>以模态方式显示确认框，返回用户选择（取消/关闭都返回 false）。</summary>
    internal static Task<bool> ShowAsync(
        Window owner,
        string title,
        string question,
        string name,
        string detail = "",
        string? note = null,
        string confirmText = "Yes",
        bool danger = true)
    {
        return Create(title, question, name, detail, note, confirmText, danger)
            .ShowDialog<bool>(owner);
    }

    /// <summary>构造并填充确认框（不显示），供实机自检直接持有实例。</summary>
    internal static ConfirmDialog Create(
        string title,
        string question,
        string name,
        string detail = "",
        string? note = null,
        string confirmText = "Yes",
        bool danger = true)
    {
        var dialog = new ConfirmDialog();
        dialog.Configure(title, question, name, detail, note, confirmText, danger);
        return dialog;
    }

    internal void Configure(
        string title,
        string question,
        string name,
        string detail,
        string? note,
        string confirmText,
        bool danger)
    {
        Title = title;
        DialogTitleText.Text = title;
        QuestionText.Text = question;

        NameText.Text = name;
        NameText.IsVisible = !string.IsNullOrWhiteSpace(name);

        DetailText.Text = detail;
        DetailText.IsVisible = NameText.IsVisible && !string.IsNullOrWhiteSpace(detail);

        NoteText.Text = note ?? "";
        NoteText.IsVisible = !string.IsNullOrWhiteSpace(note);

        ConfirmButton.Content = confirmText;
        if (danger)
        {
            ConfirmButton.Classes.Add("DialogDanger");
        }
        else
        {
            ConfirmButton.Classes.Remove("DialogDanger");
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
