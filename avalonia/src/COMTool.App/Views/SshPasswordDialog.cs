using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace COMTool.App.Views;

/// <summary>
/// Native-style SSH password prompt. The Python version asks for the password
/// in the connection dialog; Phase 1 keeps the same information flow while the
/// full session editor is ported.
/// </summary>
public sealed class SshPasswordDialog : Window
{
    private readonly TextBox _password;
    private readonly CheckBox _remember;

    public SshPasswordDialog(string target)
    {
        Title = "SSH password";
        Width = 390;
        Height = 190;
        MinWidth = 320;
        MinHeight = 170;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _password = new TextBox
        {
            PasswordChar = '\u25cf',
            PlaceholderText = "Password",
            Margin = new Thickness(0, 4, 0, 0),
        };
        _remember = new CheckBox
        {
            Content = "Remember password",
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => Submit();

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"Password for {target}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                _password,
                _remember,
                buttons,
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Submit();
            }
            else if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
        Opened += (_, _) => _password.Focus();
    }

    public string Password => _password.Text ?? "";

    public bool Remember => _remember.IsChecked == true;

    private void Submit()
    {
        if (Password.Length > 0)
        {
            Close(true);
        }
    }
}
