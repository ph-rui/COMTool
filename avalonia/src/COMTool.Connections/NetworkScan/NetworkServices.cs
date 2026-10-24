namespace COMTool.Connections.NetworkScan;

/// <summary>
/// MobaXterm-compatible service tables for the tool pages:
///  - 网络扫描仪 (network scanner): fixed service grid columns in display order
///    (SSH / RDP / VNC / FTP / Telnet / Rlogin / HTTP); only these ports are
///    probed.
///  - 端口扫描仪 (port scanner): the "仅扫描已知端口" (known ports only) list;
///    names follow MobaXterm's result style ("- Port #22 (ssh): listening").
/// </summary>
public static class NetworkServices
{
    /// <summary>
    /// 网络扫描仪 fixed service columns, in MobaXterm display order.
    /// </summary>
    public static readonly IReadOnlyList<(int Port, string Name)> ServiceColumns =
        new (int Port, string Name)[]
        {
            (22,   "SSH"),
            (3389, "RDP"),
            (5900, "VNC"),
            (21,   "FTP"),
            (23,   "Telnet"),
            (513,  "Rlogin"),
            (80,   "HTTP"),
        };

    /// <summary>Service column ports, same order as <see cref="ServiceColumns"/>.</summary>
    public static readonly int[] ServicePorts =
    {
        22, 3389, 5900, 21, 23, 513, 80,
    };

    /// <summary>
    /// 端口扫描仪 "仅扫描已知端口" list (ascending). Names mirror
    /// MobaXterm's port names, including "rdp" for 3389 and "x11 :0"
    /// for 6000.
    /// </summary>
    public static readonly IReadOnlyList<(int Port, string Name)> KnownPorts =
        new (int Port, string Name)[]
        {
            (20,    "ftp-data"),
            (21,    "ftp"),
            (22,    "ssh"),
            (23,    "telnet"),
            (25,    "smtp"),
            (53,    "domain"),
            (80,    "http"),
            (110,   "pop3"),
            (111,   "sunrpc"),
            (123,   "ntp"),
            (135,   "msrpc"),
            (139,   "netbios-ssn"),
            (143,   "imap"),
            (161,   "snmp"),
            (389,   "ldap"),
            (443,   "https"),
            (445,   "microsoft-ds"),
            (465,   "smtps"),
            (587,   "submission"),
            (993,   "imaps"),
            (995,   "pop3s"),
            (1433,  "ms-sql-s"),
            (1723,  "pptp"),
            (2375,  "rtsp"),
            (3306,  "mysql"),
            (3389,  "rdp"),
            (5432,  "postgresql"),
            (5900,  "vnc"),
            (6000,  "x11 :0"),
            (8008,  "blackberry"),
            (8080,  "http-alt"),
            (8443,  "https-alt"),
            (8888,  "http-persistent"),
            (11211, "memcache"),
            (27017, "mongodb"),
        };

    /// <summary>Known-port numbers, ascending (probe order for known mode).</summary>
    public static readonly int[] KnownPortNumbers =
    {
        20, 21, 22, 23, 25, 53, 80, 110, 111, 123, 135, 139, 143, 161, 389,
        443, 445, 465, 587, 993, 995, 1433, 1723, 2375, 3306, 3389, 5432,
        5900, 6000, 8008, 8080, 8443, 8888, 11211, 27017,
    };

    private static readonly Dictionary<int, string> PortNames =
        KnownPorts.ToDictionary(kvp => kvp.Port, kvp => kvp.Name);

    /// <summary>Service name for a port, or null when not in the known table.</summary>
    public static string? ServiceName(int port) =>
        PortNames.TryGetValue(port, out var name) ? name : null;

    /// <summary>
    /// MobaXterm result line: "- Port #22 (ssh): listening"; ports without a
    /// known name drop the "(name)" part.
    /// </summary>
    public static string FormatPortLine(int port)
    {
        var name = ServiceName(port);
        return name is null
            ? $"- Port #{port}: listening"
            : $"- Port #{port} ({name}): listening";
    }
}
