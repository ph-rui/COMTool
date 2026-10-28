using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using COMTool.App.ViewModels;

namespace COMTool.App.Services;

/// <summary>
/// Utility for parsing, formatting, and validating macro hotkeys.
/// </summary>
public static class MacroHotkey
{
    public static IReadOnlyList<string> Options()
    {
        var list = new List<string> { "<none>" };
        for (var i = 5; i <= 12; i++)
        {
            list.Add($"F{i}");
        }
        for (char c = 'A'; c <= 'Z'; c++)
        {
            list.Add($"Ctrl+Shift+{c}");
            list.Add($"Ctrl+Alt+{c}");
        }
        return list;
    }

    public static bool TryParse(string? s, out Key key, out KeyModifiers mods)
    {
        key = Key.None;
        mods = KeyModifiers.None;

        if (string.IsNullOrWhiteSpace(s) || s == "<none>")
        {
            return false;
        }

        var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Length > 3)
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= KeyModifiers.Control;
                    break;
                case "shift":
                    mods |= KeyModifiers.Shift;
                    break;
                case "alt":
                    mods |= KeyModifiers.Alt;
                    break;
                default:
                    return false;
            }
        }

        var keyPart = parts[^1].ToUpperInvariant();

        // Handle F-keys
        if (keyPart.StartsWith("F", StringComparison.Ordinal) && keyPart.Length > 1)
        {
            if (int.TryParse(keyPart[1..], out var fNum) && fNum >= 1 && fNum <= 12)
            {
                key = (Key)(Key.F1 + (fNum - 1));
            }
            else
            {
                return false;
            }
        }
        // Handle single letters and digits
        else if (keyPart.Length == 1)
        {
            var c = keyPart[0];
            if (char.IsLetter(c))
            {
                // Avalonia Key enum is NOT ascii-based: Key.A==44, Key.Z==69.
                key = (Key)((int)Key.A + (c - 'A'));
            }
            else if (char.IsDigit(c))
            {
                key = (Key)(Key.D0 + (c - '0'));
            }
            else
            {
                return false;
            }
        }
        // Try generic enum parse for other keys (Space, Enter, etc)
        else
        {
            if (!Enum.TryParse<Key>(keyPart, true, out key) || key == Key.None)
            {
                return false;
            }
        }

        return key != Key.None;
    }

    public static string Format(Key key, KeyModifiers mods)
    {
        if (key == Key.None) return "<none>";

        string keyStr = key switch
        {
            >= Key.A and <= Key.Z => ((char)('A' + (int)key - (int)Key.A)).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (int)key - (int)Key.D0)).ToString(),
            _ => key.ToString()
        };

        var modsList = new List<string>();
        if ((mods & KeyModifiers.Control) != 0) modsList.Add("Ctrl");
        if ((mods & KeyModifiers.Shift) != 0) modsList.Add("Shift");
        if ((mods & KeyModifiers.Alt) != 0) modsList.Add("Alt");

        if (modsList.Count == 0) return keyStr;
        return string.Join("+", modsList) + "+" + keyStr;
    }

    public static bool IsReserved(Key key, KeyModifiers mods)
    {
        var m = mods & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
        if (key == Key.Space && m == KeyModifiers.Control) return true;
        if (key == Key.F11 && m == KeyModifiers.None) return true;
        if (m == KeyModifiers.Control && (key == Key.W || key == Key.T || key == Key.N || key == Key.S)) return true;
        return false;
    }

    public static bool IsTaken(IEnumerable<MacroItem> macros, string? candidate, string? exceptId)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate == "<none>")
            return false;

        if (!TryParse(candidate, out var key, out var mods))
            return false;

        foreach (var m in macros)
        {
            if (m.Id == exceptId) continue;
            if (string.IsNullOrEmpty(m.Hotkey)) continue;
            if (TryParse(m.Hotkey, out var k2, out var m2) && k2 == key && m2 == mods)
                return true;
        }
        return false;
    }
}
