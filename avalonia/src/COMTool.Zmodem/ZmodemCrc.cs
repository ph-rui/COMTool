namespace COMTool.Zmodem;

public static class ZmodemCrc
{
    private static readonly ushort[] Crc16Table = BuildCrc16Table();
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc = (ushort)(((crc << 8) & 0xFFFF) ^ Crc16Table[((crc >> 8) ^ value) & 0xFF]);
        }

        return crc;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ Crc32Table[(crc ^ value) & 0xFF];
        }

        return crc ^ 0xFFFFFFFFu;
    }

    public static byte[] PackCrc16(ushort value) =>
        new[] { (byte)(value >> 8), (byte)value };

    public static ushort UnpackCrc16(ReadOnlySpan<byte> data)
    {
        if (data.Length != 2)
        {
            throw new ArgumentException("CRC16 must be 2 bytes", nameof(data));
        }

        return (ushort)((data[0] << 8) | data[1]);
    }

    public static byte[] PackCrc32(uint value) =>
        new[]
        {
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24),
        };

    public static uint UnpackCrc32(ReadOnlySpan<byte> data)
    {
        if (data.Length != 4)
        {
            throw new ArgumentException("CRC32 must be 4 bytes", nameof(data));
        }

        return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
    }

    private static ushort[] BuildCrc16Table()
    {
        var table = new ushort[256];
        for (var value = 0; value < table.Length; value++)
        {
            var crc = value << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? ((crc << 1) ^ 0x1021) & 0xFFFF
                    : (crc << 1) & 0xFFFF;
            }

            table[value] = (ushort)crc;
        }

        return table;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xEDB88320u
                    : crc >> 1;
            }

            table[value] = crc;
        }

        return table;
    }
}
