using System.IO.Ports;
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
            return new SerialPortScanResult(
                true,
                Normalize(SerialPort.GetPortNames().Select(name => new SerialPortInfo(name))));
        }
        catch (Exception ex)
        {
            return SerialPortScanResult.Failed(ex.Message);
        }
    }

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
