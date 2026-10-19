using System.Text;
using System.Text.RegularExpressions;

namespace COMTool.Zmodem;

/// <summary>
/// Injects MobaXterm's default OK/warning/error keyword colors into plain
/// terminal output. Remote ANSI foreground/background colors always win: text
/// is only decorated while both terminal attributes are at their defaults.
///
/// The highlighter works on the byte stream because Iciclecreek's terminal
/// control does not expose per-cell paint hooks. Escape sequences are passed
/// through untouched and SGR state is tracked across chunks.
/// </summary>
public sealed class MobaKeywordHighlighter
{
    private static readonly Regex UrlPattern = new(
        @"(?<![A-Za-z_&-])(https?://[A-Za-z0-9_.&?=%~#{}()@+\-]+:?[A-Za-z0-9_./&?=%~#{}()@+\-]+)(?![A-Za-z0-9_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ErrorPattern = new(
        @"(?<![A-Za-z_&-])(?:(?:bad|wrong|incorrect|improper|invalid|unsupported)(?: file| memory)? (?:descriptor|alloc(?:ation)?|addr(?:ess)?|owner(?:ship)?|arg(?:ument)?|param(?:eter)?|setting|length|filename)|not properly|improperly|(?:operation |connection |authentication |access |permission )?(?:denied|disallowed|not allowed|refused|problem|failed|failure|not permitted)|no [A-Za-z]+(?: [A-Za-z]+)? found|invalid|unsupported|not supported|seg(?:mentation )?fault|corruption|corrupted|corrupt|overflow|underrun|not ok|unimplemented|unsuccessfull|not implemented|permerrors?|fehlers?|errore|errors?|erreurs?|fejl|virhe|greška|erro|fel|\(ee\)|\(ni\))(?![A-Za-z_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SuccessPattern = new(
        @"(?<![A-Za-z_&-])(?:accepted|allowed|enabled|connected|erfolgreich|exitoso|successo|sucedido|framgångsrik|successfully|successful|succeeded|success)(?![A-Za-z_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WarningPattern = new(
        @"(?<![A-Za-z_&-])(?:\[\-w[A-Za-z-]+\]|caught signal [0-9]+|cannot|(?:connection (?:to (?:remote host|[a-z0-9.]+) )?)?(?:closed|terminated|stopped|not responding)|exited|no more [A-Za-z] available|unexpected|(?:command |binary |file )?not found|ooo?o?o?ps|out of (?:space|memory)|low (?:memory|disk)|unknown|disabled|disconnected|deprecated|refused|disconnect(?:ion)?|advertencia|avvertimento|attention|warnings?|achtung|exclamation|alerts?|warnungs?|advarsel|pedwarn|aviso|varoitus|upozorenje|peringatan|uyari|varning|avertissement|\(ww\)|\(\?\?\)|could not|unable to)(?![A-Za-z_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MagentaPattern = new(
        @"(?<![0-9A-Za-z_&-])(?:localhost|(?:[1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-4])\.[0-9]+\.[0-9]+\.[0-9]+|null|none)(?![0-9A-Za-z_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CyanPattern = new(
        @"(?<![A-Za-z_&-])(?:last (?:failed )?login:|launching|checking|loading|creating|building|important|booting|starting|notice|informational|informationen|informazioni|informação|oplysninger|informations?|info|información|informasi|note|\(ii\)|\(!!\))(?![A-Za-z_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BooleanErrorPattern = new(
        "[=>\"':.,;({\\[]\\s*(false|no|ko)(?=\\s*[]=>\"':.,;)} ]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BooleanSuccessPattern = new(
        "[=>\"':.,;({\\[]\\s*(true|yes|ok)(?=\\s*[]=>\"':.,;)} ]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Color, int Group)[] Rules =
    {
        (UrlPattern, "underline", 1),
        (ErrorPattern, "brightred", 0),
        (SuccessPattern, "brightgreen", 0),
        (WarningPattern, "brightyellow", 0),
        (MagentaPattern, "brightmagenta", 0),
        (CyanPattern, "brightcyan", 0),
        (BooleanErrorPattern, "brightred", 1),
        (BooleanSuccessPattern, "brightgreen", 1),
    };

    private static readonly byte[] Red = Sequence("38;2;255;107;107");
    private static readonly byte[] Green = Sequence("38;2;126;231;135");
    private static readonly byte[] Yellow = Sequence("38;2;255;255;102");
    private static readonly byte[] Magenta = Sequence("38;2;255;124;214");
    private static readonly byte[] Cyan = Sequence("38;2;102;224;255");
    private static readonly byte[] Underline = Sequence("4");
    private static readonly byte[] DefaultForeground = Sequence("39");
    private static readonly byte[] NoUnderline = Sequence("24");

    private static readonly string[] PhraseStarters =
    {
        "last login:", "last failed login:", "launching", "checking", "loading",
        "creating", "building", "important", "booting", "starting", "notice",
        "informational", "information", "info", "note", "connected", "success",
        "successful", "successfully", "succeeded", "accepted", "allowed",
        "enabled", "error", "failed", "failure", "invalid", "unsupported",
        "not supported", "not found", "not ok", "not implemented",
        "not properly", "connection refused", "connection closed",
        "connection terminated", "connection stopped", "disconnected",
        "warning", "cannot", "could not", "unable to", "unknown",
        "out of memory", "out of space", "localhost", "http://", "https://",
    };

    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly List<byte> _escapeCarry = new();
    private string _textCarry = "";
    private bool _foregroundDefault = true;
    private bool _backgroundDefault = true;

    public byte[] Transform(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        // Binary serial traffic is not line-oriented text. Do not risk
        // injecting escape sequences into it.
        foreach (var value in data)
        {
            if (value == 0)
            {
                return data.ToArray();
            }
        }

        var input = new byte[_escapeCarry.Count + data.Length];
        _escapeCarry.CopyTo(input, 0);
        data.CopyTo(input.AsSpan(_escapeCarry.Count));
        _escapeCarry.Clear();

        var output = new List<byte>(input.Length + 32);
        var text = new List<byte>();
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] != 0x1B)
            {
                text.Add(input[index]);
                continue;
            }

            FlushText(text, output, allowHold: false);
            if (index + 1 >= input.Length)
            {
                _escapeCarry.Add(input[index]);
                break;
            }

            if (input[index + 1] == (byte)'[')
            {
                var end = index + 2;
                while (end < input.Length && (input[end] < 0x40 || input[end] > 0x7E))
                {
                    end++;
                }

                if (end >= input.Length)
                {
                    _escapeCarry.AddRange(input.AsSpan(index));
                    break;
                }

                output.AddRange(input.AsSpan(index, end - index + 1));
                UpdateSgr(input.AsSpan(index, end - index + 1));
                index = end;
                continue;
            }

            if (input[index + 1] == (byte)']')
            {
                var end = index + 2;
                var found = false;
                while (end < input.Length)
                {
                    if (input[end] == 0x07)
                    {
                        found = true;
                        break;
                    }

                    if (input[end] == 0x1B
                        && end + 1 < input.Length
                        && input[end + 1] == (byte)'\\')
                    {
                        found = true;
                        end++;
                        break;
                    }

                    end++;
                }

                if (!found)
                {
                    _escapeCarry.AddRange(input.AsSpan(index));
                    break;
                }

                output.AddRange(input.AsSpan(index, end - index + 1));
                index = end;
                continue;
            }

            output.AddRange(input.AsSpan(index, 2));
            index++;
        }

        FlushText(text, output, allowHold: true);
        return output.ToArray();
    }

    private void FlushText(List<byte> bytes, List<byte> output, bool allowHold)
    {
        if (bytes.Count == 0 && _textCarry.Length == 0)
        {
            return;
        }

        var raw = bytes.ToArray();
        bytes.Clear();
        var charCount = _decoder.GetCharCount(raw, 0, raw.Length, flush: false);
        var chars = new char[charCount];
        _decoder.GetChars(raw, 0, raw.Length, chars, 0, flush: false);

        var decoded = new string(chars);
        if (decoded.Contains('\uFFFD'))
        {
            // Not valid UTF-8 (for example a GBK serial stream). Preserve the
            // original bytes rather than replacing them with U+FFFD.
            if (_textCarry.Length > 0)
            {
                output.AddRange(Encoding.UTF8.GetBytes(_textCarry));
                _textCarry = "";
            }

            _decoder.Reset();
            output.AddRange(raw);
            return;
        }

        var text = _textCarry + decoded;
        _textCarry = "";
        var hold = allowHold ? FindHoldLength(text) : 0;
        if (hold > 0)
        {
            _textCarry = text[^hold..];
            text = text[..^hold];
        }

        if (text.Length == 0)
        {
            return;
        }

        if (!_foregroundDefault || !_backgroundDefault)
        {
            output.AddRange(Encoding.UTF8.GetBytes(text));
            return;
        }

        EmitHighlighted(text, output);
    }

    private static int FindHoldLength(string text)
    {
        if (text.Length == 0 || text[^1] is '\r' or '\n')
        {
            return 0;
        }

        foreach (var phrase in PhraseStarters)
        {
            if (text.EndsWith(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        var max = Math.Min(24, text.Length);
        for (var length = max; length > 0; length--)
        {
            var tail = text[^length..];
            foreach (var phrase in PhraseStarters)
            {
                if (tail.Length < phrase.Length
                    && phrase.StartsWith(tail, StringComparison.OrdinalIgnoreCase))
                {
                    return length;
                }
            }
        }

        return 0;
    }

    private static void EmitHighlighted(string text, List<byte> output)
    {
        var colors = new string?[text.Length];
        foreach (var (pattern, color, group) in Rules)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var captured = match.Groups[group];
                for (var i = captured.Index;
                     i < captured.Index + captured.Length && i < colors.Length;
                     i++)
                {
                    colors[i] ??= color;
                }
            }
        }

        var hasColor = false;
        foreach (var value in colors)
        {
            if (value is not null)
            {
                hasColor = true;
                break;
            }
        }

        if (!hasColor)
        {
            output.AddRange(Encoding.UTF8.GetBytes(text));
            return;
        }

        var index = 0;
        while (index < colors.Length)
        {
            var color = colors[index];
            if (color is null)
            {
                var start = index;
                while (index < colors.Length && colors[index] is null)
                {
                    index++;
                }

                output.AddRange(Encoding.UTF8.GetBytes(text, start, index - start));
                continue;
            }

            var end = index;
            while (end < colors.Length && colors[end] == color)
            {
                end++;
            }

            output.AddRange(color switch
            {
                "underline" => Underline,
                "brightred" => Red,
                "brightgreen" => Green,
                "brightyellow" => Yellow,
                "brightmagenta" => Magenta,
                _ => Cyan,
            });
            output.AddRange(Encoding.UTF8.GetBytes(text, index, end - index));
            output.AddRange(color == "underline" ? NoUnderline : DefaultForeground);
            index = end;
        }
    }

    private void UpdateSgr(ReadOnlySpan<byte> sequence)
    {
        // ESC [ params m
        if (sequence.Length < 3 || sequence[^1] != (byte)'m')
        {
            return;
        }

        var body = Encoding.ASCII.GetString(sequence[2..^1]);
        if (body.Length == 0)
        {
            body = "0";
        }

        var parts = body.Split(';');
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Contains(':'))
            {
                if (part.StartsWith("38:", StringComparison.Ordinal))
                {
                    _foregroundDefault = false;
                }
                else if (part.StartsWith("48:", StringComparison.Ordinal))
                {
                    _backgroundDefault = false;
                }

                continue;
            }

            if (!int.TryParse(part, out var value))
            {
                continue;
            }

            if (value == 0)
            {
                _foregroundDefault = true;
                _backgroundDefault = true;
            }
            else if (value == 39)
            {
                _foregroundDefault = true;
            }
            else if (value == 49)
            {
                _backgroundDefault = true;
            }
            else if ((value >= 30 && value <= 37) || (value >= 90 && value <= 97))
            {
                _foregroundDefault = false;
            }
            else if (value is >= 40 and <= 47 or >= 100 and <= 107)
            {
                _backgroundDefault = false;
            }
            else if (value == 38)
            {
                _foregroundDefault = false;
                index += index + 1 < parts.Length && parts[index + 1] == "5" ? 2 : 4;
            }
            else if (value == 48)
            {
                _backgroundDefault = false;
                index += index + 1 < parts.Length && parts[index + 1] == "5" ? 2 : 4;
            }
        }
    }

    private static byte[] Sequence(string parameters) =>
        Encoding.ASCII.GetBytes($"\u001b[{parameters}m");
}
