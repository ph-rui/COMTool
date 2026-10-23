using System.Net;

namespace COMTool.Connections.NetworkScan;

/// <summary>
/// One probed host. A host is reported when at least one of the probes
/// succeeded: an ICMP reply or an open TCP port.
/// </summary>
public sealed record NetworkScanHost(
    IPAddress Address,
    string? HostName,
    IReadOnlyList<int> OpenPorts,
    int? RoundTripMs);

/// <summary>
/// Scan progress. Host is non-null when the just-finished probe found the
/// host to be up.
/// </summary>
public sealed record NetworkScanProgress(
    int Completed,
    int Total,
    NetworkScanHost? Host);
