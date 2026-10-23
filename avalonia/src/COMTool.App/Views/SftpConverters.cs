using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
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
/// Shows the size of an entry in kilobytes, as the size column of
/// MobaXterm does. A directory has no size of its own.
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

        // A file of one byte still occupies one kilobyte of the column,
        // so the value is rounded up.
        return ((entry.Size + 1023) / 1024).ToString(culture);
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
/// The icon in front of the name of an entry: a folder, a file or a
/// symbolic link. The glyphs come from the FontAwesome font of the
/// application.
/// </summary>
public sealed class SftpEntryIconConverter : IValueConverter
{
    public static readonly SftpEntryIconConverter Instance = new();

    /// <summary>The glyph of a folder.</summary>
    public const string FolderGlyph = "\uf114";

    /// <summary>The glyph of a file.</summary>
    public const string FileGlyph = "\uf016";

    /// <summary>The glyph of a symbolic link.</summary>
    public const string LinkGlyph = "\uf0c1";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SftpEntry entry)
        {
            return string.Empty;
        }

        if (entry.IsSymbolicLink)
        {
            return LinkGlyph;
        }

        return entry.IsDirectory ? FolderGlyph : FileGlyph;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The colour of the icon in front of the name of an entry.
/// </summary>
public sealed class SftpEntryIconBrushConverter : IValueConverter
{
    public static readonly SftpEntryIconBrushConverter Instance = new();

    private static readonly IBrush FolderBrush = new SolidColorBrush(Color.Parse("#d99820"));
    private static readonly IBrush FileBrush = new SolidColorBrush(Color.Parse("#6d6d6d"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#2b579a"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SftpEntry entry)
        {
            return FileBrush;
        }

        if (entry.IsSymbolicLink)
        {
            return LinkBrush;
        }

        return entry.IsDirectory ? FolderBrush : FileBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
