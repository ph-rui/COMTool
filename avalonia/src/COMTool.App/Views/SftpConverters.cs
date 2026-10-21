using System.Globalization;
using Avalonia.Data.Converters;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// Shows the name of an entry. A symbolic link carries a marker, so that
/// the user sees that it points somewhere else.
/// </summary>
public sealed class SftpEntryNameConverter : IValueConverter
{
    public static readonly SftpEntryNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SftpEntry entry)
        {
            return entry.IsSymbolicLink ? entry.Name + "@" : entry.Name;
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows the size of an entry. A directory has no size of its own.
/// </summary>
public sealed class SftpEntrySizeConverter : IValueConverter
{
    public static readonly SftpEntrySizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SftpEntry entry || entry.IsDirectory)
        {
            return string.Empty;
        }

        var size = (double)entry.Size;
        if (size < 1024)
        {
            return $"{entry.Size} B";
        }

        size /= 1024;
        if (size < 1024)
        {
            return $"{size:0.#} KB";
        }

        size /= 1024;
        if (size < 1024)
        {
            return $"{size:0.#} MB";
        }

        size /= 1024;
        return $"{size:0.#} GB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows when an entry was written the last time.
/// </summary>
public sealed class SftpEntryTimeConverter : IValueConverter
{
    public static readonly SftpEntryTimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SftpEntry entry)
        {
            return string.Empty;
        }

        return entry.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows the nine permission bits of an entry in the form rwxr-xr-x.
/// </summary>
public sealed class SftpEntryPermissionConverter : IValueConverter
{
    public static readonly SftpEntryPermissionConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SftpEntry entry)
        {
            return string.Empty;
        }

        return entry.Permission.ToRwxString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The text of the tip of the button that folds the drawer away.
/// </summary>
public sealed class SftpToggleToolTipConverter : IValueConverter
{
    public static readonly SftpToggleToolTipConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Hide SFTP browser" : "SFTP browser";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
