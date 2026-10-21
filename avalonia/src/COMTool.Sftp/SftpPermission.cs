namespace COMTool.Sftp;

/// <summary>
/// The nine permission bits of a remote entry. The execute bit is
/// independent of the read bit: 0111 means execute only.
/// </summary>
public readonly record struct SftpPermission(
    bool OwnerRead,
    bool OwnerWrite,
    bool OwnerExecute,
    bool GroupRead,
    bool GroupWrite,
    bool GroupExecute,
    bool OthersRead,
    bool OthersWrite,
    bool OthersExecute)
{
    public static SftpPermission FromOctal(int mode)
    {
        var bits = mode & 0b111_111_111;
        var owner = (bits >> 6) & 0b111;
        var group = (bits >> 3) & 0b111;
        var others = bits & 0b111;

        return new SftpPermission(
            (owner & 0b100) != 0,
            (owner & 0b010) != 0,
            (owner & 0b001) != 0,
            (group & 0b100) != 0,
            (group & 0b010) != 0,
            (group & 0b001) != 0,
            (others & 0b100) != 0,
            (others & 0b010) != 0,
            (others & 0b001) != 0);
    }

    public int ToOctal()
    {
        var owner = (OwnerRead ? 0b100 : 0)
            | (OwnerWrite ? 0b010 : 0)
            | (OwnerExecute ? 0b001 : 0);
        var group = (GroupRead ? 0b100 : 0)
            | (GroupWrite ? 0b010 : 0)
            | (GroupExecute ? 0b001 : 0);
        var others = (OthersRead ? 0b100 : 0)
            | (OthersWrite ? 0b010 : 0)
            | (OthersExecute ? 0b001 : 0);

        return (owner << 6) | (group << 3) | others;
    }

    /// <summary>
    /// Parses an octal mode written as digits, for example "640".
    /// </summary>
    public static SftpPermission FromOctalText(string octalText)
    {
        var digits = (octalText ?? string.Empty).Trim();
        return FromOctal(Convert.ToInt32(digits, 8));
    }

    /// <summary>
    /// Renders the mode as three octal digits, for example "640".
    /// </summary>
    public string ToOctalText()
    {
        return Convert.ToString(ToOctal(), 8).PadLeft(3, '0');
    }

    public string ToRwxString()
    {
        return string.Concat(
            Group(OwnerRead, OwnerWrite, OwnerExecute),
            Group(GroupRead, GroupWrite, GroupExecute),
            Group(OthersRead, OthersWrite, OthersExecute));
    }

    public static SftpPermission FromRwxString(string text)
    {
        var source = text ?? string.Empty;
        var owner = Read(source, 0);
        var group = Read(source, 3);
        var others = Read(source, 6);

        return new SftpPermission(
            owner.Read,
            owner.Write,
            owner.Execute,
            group.Read,
            group.Write,
            group.Execute,
            others.Read,
            others.Write,
            others.Execute);
    }

    private static string Group(bool read, bool write, bool execute)
    {
        return string.Concat(
            read ? "r" : "-",
            write ? "w" : "-",
            execute ? "x" : "-");
    }

    private static (bool Read, bool Write, bool Execute) Read(string source, int offset)
    {
        return (
            Has(source, offset, 'r'),
            Has(source, offset + 1, 'w'),
            Has(source, offset + 2, 'x'));
    }

    private static bool Has(string source, int index, char expected)
    {
        return index < source.Length && source[index] == expected;
    }
}
