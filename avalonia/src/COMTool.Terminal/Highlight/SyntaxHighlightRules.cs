using System.Text.RegularExpressions;

namespace COMTool.Terminal.Highlight;

/// <summary>
/// 语法高亮方案（对齐 MobaXterm「语法高亮」子菜单 6 选项）。
/// PerLine 已从菜单移除（r40.2：MobaXterm 公开资料无确切语义，用户决定去掉）；
/// 保留枚举值仅为 config.json 兼容（"per_line" 映射后无规则，行为同 default）。
/// </summary>
public enum HighlightScheme
{
    Default = 0,
    ErrorWarningSuccess = 1,
    UnixShell = 2,
    Cisco = 3,
    PerLine = 4,
    Sql = 5,
}

/// <summary>方案 ↔ config.json 键名（terminal.syntaxHighlight）。</summary>
public static class HighlightSchemes
{
    public static string KeyOf(HighlightScheme scheme) => scheme switch
    {
        HighlightScheme.Default => "default",
        HighlightScheme.ErrorWarningSuccess => "error_warning",
        HighlightScheme.UnixShell => "unix_shell",
        HighlightScheme.Cisco => "cisco",
        HighlightScheme.PerLine => "per_line",
        HighlightScheme.Sql => "sql",
        _ => "default",
    };

    public static HighlightScheme FromName(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "error_warning" or "ews" => HighlightScheme.ErrorWarningSuccess,
            "unix_shell" or "unix" => HighlightScheme.UnixShell,
            "cisco" => HighlightScheme.Cisco,
            "per_line" or "perline" => HighlightScheme.PerLine,
            "sql" => HighlightScheme.Sql,
            _ => HighlightScheme.Default,
        };
    }
}

/// <summary>一个匹配段：行内字符区间 [Start, End) + 调色板色号（1红2绿3黄4蓝5品红6青）。</summary>
public sealed record HighlightSpan(int Start, int End, int Color, string Text);

/// <summary>
/// 语法高亮规则引擎（纯函数，可 headless 测试）。
/// 规则集照抄 MobaXterm 默认 CustomSyntax 定义（从 MobaXterm.ini 导出的正则/关键字表），
/// 与远端 shell/程序无关——MobaXterm 的“live syntax coloration”即此类模式匹配。
/// 规则按声明顺序优先：先声明者抢占字符区间，后声明者不得重叠（与 MobaXterm 多色组
/// 互斥的行为一致，如行注释整行着色后，行内关键词不再参与）。
/// </summary>
public static class SyntaxHighlightRules
{
    private sealed class Rule
    {
        public required Regex Regex;
        public required int Color;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Error/Warning/Success keywords（MobaXterm「OK/warning/error keywords」默认规则）──
    // 注意顺序：短语规则必须先于单词规则——抢占机制下同色组内先声明者独占区间
    // （否则 "permission denied" 的 "denied" 被单词规则先占，短语只剩半句）。
    private static readonly Rule[] EwsRules =
    {
        new() { Color = 1, Regex = Rx(@"\b(operation|connection|authentication|access|permission|file|memory) (denied|disallowed|not allowed|refused|problem|failure|corrupt(ed|ion)?)\b") },
        new() { Color = 1, Regex = Rx(@"\bno [a-z]+( [a-z]+)? found\b") },
        new() { Color = 1, Regex = Rx(@"\bbad (file |memory )?(descriptor|allocation|address|owner(ship)?|argument|parameter|setting|length|filename)\b") },
        new() { Color = 1, Regex = Rx(@"\b(error|errors|errore|fehler|fel|fejl|fatal|crash(ed|es)?|crashing|seg ?fault|corrupt|corrupted|corruption|denied|refused|failed|failure|failures|invalid|unsupported|not supported|not allowed|disallowed|not permitted|overflow|underflow|not implemented|unimplemented|not ok|abort(ing|ed)?|critical)\b") },
        new() { Color = 3, Regex = Rx(@"\b(warn|warning|warnings|warned|caution|attention|advice|unknown|unexpected|deprecat(ed|ion|es)?|could not|unable to|cannot|out of (space|memory|disk)|low (memory|disk|space)|no [a-z]+ available|disconnected|disconnect|not responding|closed|terminated|stopped|exited|oops|not found|missing)\b") },
        new() { Color = 3, Regex = Rx(@"\bcaught signal \d+\b") },
        new() { Color = 2, Regex = Rx(@"\b(success|successful|successfully|succeeded|succeed|ok|okay|yes|true|enabled|connected|accept(ed|ing)?|allow(ed|ing)?|complet(ed|ion))\b") },
        new() { Color = 5, Regex = Rx(@"\blocalhost\b") },
        new() { Color = 5, Regex = Rx(@"\b(?:\d{1,3}\.){3}\d{1,3}\b") },
        // r56：MAC 地址→品红（复刻用户 MobaXterm 的个人高亮规则，ifconfig
        // ether 行的 82:37:a3:fe:87:0b 等；支持冒号/连字符两种分隔）
        new() { Color = 5, Regex = Rx(@"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b") },
        new() { Color = 6, Regex = Rx(@"\b(info|information|informational|notice|launching|checking|loading|creating|building|booting|starting|started|important|debug|last (failed )?login)\b") },
    };

    // ── Unix shell script（MobaXterm「Custom: Unix shell」默认规则）──
    private static readonly Rule[] UnixRules =
    {
        new() { Color = 2, Regex = Rx(@"^\s*#.*$") },                                   // 行注释→绿（整行）
        new() { Color = 1, Regex = Rx(@"\$\([^()]*\)|`[^`]+`") },                        // 命令替换→红
        new() { Color = 3, Regex = Rx(@"\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*") },        // 变量→黄
        new() { Color = 5, Regex = Rx(@"\b(if|then|else|elif|fi|for|in|do|done|case|esac|function|return|exit|break|continue|while|until|select|coproc)\b") },
        new() { Color = 6, Regex = Rx(@"\b(echo|printf|export|set|unset|alias|unalias|source|cd|pwd|read|wait|trap|shift|local|readonly|declare|shopt|eval|exec|test|kill|ls|cat|grep|head|tail|type)\b") },
    };

    // ── Cisco（网络配置）（MobaXterm「Custom: Cisco (network)」默认规则）──
    private static readonly Rule[] CiscoRules =
    {
        new() { Color = 4, Regex = Rx(@"^\s*!.*$") },                                   // ! 行注释→蓝（整行）
        new() { Color = 1, Regex = Rx(@"^\s*no\b") },                                    // 行首 no→红（含前导空白，空格着前景色不可见，无害）
        new() { Color = 1, Regex = Rx(@"\b(shutdown|down|disabled|unknown|fault|error|failed|denied|refused|problem|failure|not permitted|not allowed|disallowed|notconnect)\b") },
        new() { Color = 2, Regex = Rx(@"^\s*(description|hostname|version|system).*$") }, // 整行
        new() { Color = 2, Regex = Rx(@"\b(enabled|connected|up|yes|ok)\b") },
        new() { Color = 3, Regex = Rx(@"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b") },        // MAC→黄
        new() { Color = 3, Regex = Rx(@"\b(?:\d{1,3}\.){3}\d{1,3}\b") },                  // IP→黄
        new() { Color = 3, Regex = Rx(@"\bvlan\s*\d+\b") },
        new() { Color = 3, Regex = Rx(@"\b(\w+thernet|ge|fa|gi|te|se)\d+(?:/\d+)*\b") },   // 接口号→黄
        new() { Color = 3, Regex = Rx(@"\blocalhost\b") },
        new() { Color = 5, Regex = Rx(@"\b(policy-map|class-map|class|global|logging|interface|failover|static|security-level|service-policy|spanning-tree|switchport|access-list|access-group)\b") },
        new() { Color = 6, Regex = Rx(@"\b(link-updown|vlan(-range)?|route|port-forward|mtu|show|encapsulation|rate-limit|speed|duplex|autoneg|snmp-server|media-type|ip address|ip|monitor(session)?|ping|traceroute)\b") },
    };

    // ── SQL 语法（MobaXterm「Custom: SQL」默认关键字表，五色）──
    private static readonly Rule[] SqlRules =
    {
        new() { Color = 1, Regex = Rx(@"\b(error|warning|cancel|cancelled|disable|drop|abort|after|asc|attach|before|begin|cascade|cluster|conflict|copy|cross|database|deferred|delimiters|desc|detach|each|end|explain|fail|for|full|ignore|immediate|initially|inner|instead|key|left|match|natural|of|offset|outer|pragma|raise|replace|restrict|right|row|statement|temp|temporary|trigger|vacuum|view)\b") },
        new() { Color = 2, Regex = Rx(@"\b(enable|mysql|postgresql|postgre|oracle|server|db|ok|successful|success)\b") },
        new() { Color = 3, Regex = Rx(@"\b(mode insert|lock|unlock|databases|dump|echo|exit|header|help|indices|mode|nullvalue|output|prompt|quit|read|separator|show|schema|tables|timeout|width|default|notnull|null|auto_increment)\b") },
        new() { Color = 5, Regex = Rx(@"\b(charset|engine|table|dumping|dropping|_rowid_|main|oid|rowid|sqlite_master|sqlite_temp_master)\b") },
        new() { Color = 6, Regex = Rx(@"\b(if exists|inner join|alter|all|and|as|between|by|case|collate|commit|constraint|create|deferrable|delete|distinct|else|except|foreign|from|glob|group|having|check|in|index|insert|intersect|into|is|isnull|join|like|limit|on|or|order|primary|references|rollback|select|set|then|transaction|union|unique|update|using|values|when|where)\b") },
    };

    private static Rule[] RulesFor(HighlightScheme scheme) => scheme switch
    {
        HighlightScheme.ErrorWarningSuccess => EwsRules,
        HighlightScheme.UnixShell => UnixRules,
        HighlightScheme.Cisco => CiscoRules,
        HighlightScheme.Sql => SqlRules,
        _ => Array.Empty<Rule>(),
    };

    /// <summary>
    /// 在单行文本中查找所有高亮段。按规则顺序抢占字符区间，段与段互不重叠。
    /// 纯函数：同一输入恒得同一输出（headless 可测）。
    /// </summary>
    public static List<HighlightSpan> FindSpans(string text, HighlightScheme scheme)
    {
        var result = new List<HighlightSpan>();
        var rules = RulesFor(scheme);
        if (text.Length == 0 || rules.Length == 0)
        {
            return result;
        }

        var claimed = new bool[text.Length];
        foreach (var rule in rules)
        {
            foreach (Match m in rule.Regex.Matches(text))
            {
                if (m.Length == 0)
                {
                    continue;
                }

                var start = m.Index;
                var end = m.Index + m.Length;
                var overlaps = false;
                for (var i = start; i < end; i++)
                {
                    if (claimed[i])
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                {
                    continue;
                }

                for (var i = start; i < end; i++)
                {
                    claimed[i] = true;
                }

                result.Add(new HighlightSpan(start, end, rule.Color, m.Value));
            }
        }

        return result;
    }
}
