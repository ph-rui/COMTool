using Avalonia;
using Avalonia.Controls;
using COMTool.App.Styles;

namespace COMTool.App.Services;

/// <summary>
/// Python stores the selected style in config.json as "light" or "dark".
/// Avalonia keeps the same names and swaps the resource dictionary at startup.
/// </summary>
/// <remarks>
/// r52 (AOT): 原先通过 new ResourceInclude(avares URI) 在运行时动态加载主题 XAML，
/// Native AOT 下运行时 XAML 解析器被裁剪，会抛
/// "No precompiled XAML found" 异常。现改为直接实例化带 x:Class 的主题类型
/// （ThemeLight/ThemeDark，见 Styles 目录），其构造函数走 AvaloniaXamlLoader.Load(this)
/// 预编译加载路径，AOT 与 JIT 两种模式均可用，同时消除 IL2026 警告。
/// </remarks>
public static class ThemeManager
{
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

        ResourceDictionary theme = Normalize(skin) == "dark" ? new ThemeDark() : new ThemeLight();
        var dictionaries = application.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(theme);
        }
        else
        {
            dictionaries[0] = theme;
        }
    }
}
