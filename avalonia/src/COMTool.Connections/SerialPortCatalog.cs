using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using COMTool.Core.Config;

namespace COMTool.Connections;

public sealed record SerialPortInfo(
    string Device,
    string Description = "",
    string Name = "",
    string Manufacturer = "",
    string Hwid = "")
{
    public string DisplayLabel
    {
        get
        {
            var label = Clean(Description);
            if (label.Length == 0)
            {
                label = Clean(Name);
            }

            if (label.Length == 0)
            {
                label = Clean(Manufacturer);
            }

            if (Device.Length > 0)
            {
                label = RemoveTrailingDevice(label, Device);
            }

            return string.Equals(label, Device, StringComparison.OrdinalIgnoreCase)
                ? ""
                : label;
        }
    }

    public string DisplayText
    {
        get
        {
            var label = DisplayLabel;
            return label.Length == 0 ? Device : $"{Device}  {label}";
        }
    }

    private static string Clean(string? value)
    {
        var text = string.Join(' ', (value ?? "").Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return text.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? ""
            : text;
    }

    private static string RemoveTrailingDevice(string label, string device)
    {
        var suffix = $"({device})";
        if (!label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return label[..^suffix.Length].TrimEnd();
    }
}

public sealed record SerialPortScanResult(
    bool Success,
    IReadOnlyList<SerialPortInfo> Ports,
    string? Error = null)
{
    public static SerialPortScanResult Failed(string error) =>
        new(false, Array.Empty<SerialPortInfo>(), error);
}

public static class SerialPortCatalog
{
    private static readonly string[] IgnoredDevices =
    {
        "/dev/cu.Bluetooth-Incoming-Port",
        "/dev/tty.Bluetooth-Incoming-Port",
    };

    /// <summary>
    /// Enumerates ports. A failed scan is explicitly represented so callers
    /// can preserve remembered sessions instead of pruning them.
    /// </summary>
    public static SerialPortScanResult Scan()
    {
        try
        {
            var names = SerialPort.GetPortNames();
            if (!OperatingSystem.IsWindows())
            {
                return new SerialPortScanResult(
                    true,
                    Normalize(names.Select(name => new SerialPortInfo(name))));
            }

            var descriptions = WindowsSerialPortDescriptions.Read();
            var ports = names.Select(name =>
            {
                descriptions.TryGetValue(name, out var info);
                return info ?? new SerialPortInfo(name);
            });
            return new SerialPortScanResult(true, Normalize(ports));
        }
        catch (Exception ex)
        {
            return SerialPortScanResult.Failed(ex.Message);
        }
    }

    public static string? FindComPort(string? friendlyName) =>
        WindowsSerialPortDescriptions.FindComPort(friendlyName);

    public static IReadOnlyList<SerialPortInfo> Normalize(IEnumerable<SerialPortInfo> ports)
    {
        var filtered = ports
            .Where(port => !string.IsNullOrWhiteSpace(port.Device))
            .Where(port => !IgnoredDevices.Contains(port.Device, StringComparer.Ordinal))
            .ToList();

        // macOS exposes /dev/cu.X and /dev/tty.X for the same device. Prefer
        // the callout entry, matching the Python port_utils behavior.
        var callouts = filtered
            .Where(port => port.Device.StartsWith("/dev/cu.", StringComparison.Ordinal))
            .Select(port => port.Device["/dev/cu.".Length..])
            .ToHashSet(StringComparer.Ordinal);
        if (callouts.Count > 0)
        {
            filtered = filtered
                .Where(port => !(
                    port.Device.StartsWith("/dev/tty.", StringComparison.Ordinal)
                    && callouts.Contains(port.Device["/dev/tty.".Length..])))
                .ToList();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return filtered
            .Where(port => seen.Add(port.Device))
            .OrderBy(port => port.Device, NaturalStringComparer.Instance)
            .ThenBy(port => port.DisplayLabel, NaturalStringComparer.Instance)
            .ToList();
    }
}

/// <summary>
/// Reads the friendly names for present COM ports from the Windows SetupAPI.
/// This is the equivalent of Python's serial.tools.list_ports descriptions and
/// is deliberately best-effort: a failure only hides descriptions, it never
/// makes the scan fail.
/// </summary>
internal static class WindowsSerialPortDescriptions
{
    private const uint DigcfPresent = 0x2;
    private const uint SpdrpDeviceDesc = 0x0;
    private const uint SpdrpManufacturer = 0xB;
    private const uint SpdrpFriendlyName = 0xC;
    private const uint SpdrpHardwareId = 0x1;

    private static readonly Guid PortsClassGuid =
        new("4d36e978-e325-11ce-bfc1-08002be10318");

    public static Dictionary<string, SerialPortInfo> Read()
    {
        var result = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        var deviceInfoSet = IntPtr.Zero;
        try
        {
            var portsClassGuid = PortsClassGuid;
            deviceInfoSet = SetupDiGetClassDevs(
                ref portsClassGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DigcfPresent);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            {
                return result;
            }

            var data = new SpDevinfoData
            {
                Size = (uint)Marshal.SizeOf<SpDevinfoData>(),
            };
            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data); index++)
            {
                var friendlyName = GetProperty(deviceInfoSet, ref data, SpdrpFriendlyName);
                var device = FindComPort(friendlyName);
                if (device is null || result.ContainsKey(device))
                {
                    continue;
                }

                result[device] = new SerialPortInfo(
                    device,
                    Description: friendlyName,
                    Name: GetProperty(deviceInfoSet, ref data, SpdrpDeviceDesc),
                    Manufacturer: GetProperty(deviceInfoSet, ref data, SpdrpManufacturer),
                    Hwid: GetProperty(deviceInfoSet, ref data, SpdrpHardwareId));
            }
        }
        catch
        {
            // Descriptions are optional; keep the port list usable.
        }
        finally
        {
            if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != new IntPtr(-1))
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        return result;
    }

    internal static string? FindComPort(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        var start = friendlyName.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            start = friendlyName.LastIndexOf("COM", StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0)
        {
            return null;
        }

        if (friendlyName[start] == '(')
        {
            start++;
        }

        var end = start;
        while (end < friendlyName.Length && char.IsLetterOrDigit(friendlyName[end]))
        {
            end++;
        }

        return end > start ? friendlyName[start..end].ToUpperInvariant() : null;
    }

    private static string GetProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData data,
        uint property)
    {
        uint propertyType;
        uint required;
        _ = SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref data,
            property,
            out propertyType,
            null,
            0,
            out required);
        if (required == 0 || required > 16 * 1024)
        {
            return "";
        }

        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref data,
                property,
                out propertyType,
                buffer,
                required,
                out required))
        {
            return "";
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, (uint)buffer.Length))
            .TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
