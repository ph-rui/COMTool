using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace COMTool.App.Views;

/// <summary>
/// MobaXterm highlights the gear button of the selected server row with a
/// blue border; unselected rows keep the plain grey button border.
/// </summary>
public sealed class GearBorderBrushConverter : IValueConverter
{
    public static readonly GearBorderBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#0078d4"))
            : new SolidColorBrush(Color.Parse("#adadad"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GearBorderThicknessConverter : IValueConverter
{
    public static readonly GearBorderThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new Thickness(2) : new Thickness(1);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// MobaXterm 选中行的齿轮按钮：边框高亮的同时按钮底色变白；未选中保持浅灰。
/// </summary>
public sealed class GearFaceBrushConverter : IValueConverter
{
    public static readonly GearFaceBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#ffffff"))
            : new SolidColorBrush(Color.Parse("#e1e1e1"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
