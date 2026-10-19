using System.Text.RegularExpressions;

namespace COMTool.Core.Config;

/// <summary>
/// Python-compatible natural ordering: COM2 sorts before COM10, and
/// /dev/ttyUSB2 sorts before /dev/ttyUSB10.
/// </summary>
public sealed partial class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var left = Tokenize(x);
        var right = Tokenize(y);
        var count = Math.Min(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var a = left[i];
            var b = right[i];
            int result;
            if (a.IsNumber && b.IsNumber)
            {
                result = CompareNumbers(a.Text, b.Text);
            }
            else if (a.IsNumber != b.IsNumber)
            {
                result = a.IsNumber.CompareTo(b.IsNumber);
            }
            else
            {
                result = string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
            {
                return result;
            }
        }

        var lengthResult = left.Count.CompareTo(right.Count);
        return lengthResult != 0
            ? lengthResult
            : string.Compare(x, y, StringComparison.Ordinal);
    }

    private static int CompareNumbers(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        if (left.Length != right.Length)
        {
            return left.Length.CompareTo(right.Length);
        }

        var result = string.CompareOrdinal(left, right);
        return result != 0 ? result : 0;
    }

    private static List<Token> Tokenize(string value)
    {
        var tokens = new List<Token>();
        var index = 0;
        foreach (Match match in NumberPattern().Matches(value))
        {
            if (match.Index > index)
            {
                tokens.Add(new Token(value[index..match.Index], false));
            }

            tokens.Add(new Token(match.Value, true));
            index = match.Index + match.Length;
        }

        if (index < value.Length)
        {
            tokens.Add(new Token(value[index..], false));
        }

        if (tokens.Count == 0)
        {
            tokens.Add(new Token("", false));
        }

        return tokens;
    }

    private readonly record struct Token(string Text, bool IsNumber);

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
