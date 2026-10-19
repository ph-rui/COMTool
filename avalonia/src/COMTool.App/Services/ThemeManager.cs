using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace COMTool.App.Services;

/// <summary>
/// Python stores the selected style in config.json as "light" or "dark".
/// Avalonia keeps the same names and swaps the resource dictionary at startup.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri LightTheme =
        new("avares://COMTool.App/Styles/ThemeLight.axaml");

    private static readonly Uri DarkTheme =
        new("avares://COMTool.App/Styles/ThemeDark.axaml");

    public static string Normalize(string? skin) =>
        string.Equals(skin, "dark", StringComparison.OrdinalIgnoreCase)
            ? "dark"
            : "light";

    public static void Apply(string? skin)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var source = Normalize(skin) == "dark" ? DarkTheme : LightTheme;
        var include = new ResourceInclude(source) { Source = source };
        var dictionaries = application.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(include);
        }
        else
        {
            dictionaries[0] = include;
        }
    }
}
