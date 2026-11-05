using Avalonia;
using Avalonia.Controls;

namespace COMTool.App.Styles;

/// <summary>
/// r52: 为 ThemeLight.axaml 提供 x:Class 部分类，使预编译 XAML
/// 在 Native AOT 下可被 RuntimeResourceInclude 解析（无需运行时 XAML 解析器）。
/// </summary>
public partial class ThemeLight : ResourceDictionary
{
    public ThemeLight()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
