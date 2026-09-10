using System.Text.Json;

namespace TuiEdit;

/// <summary>Combination: key + modifiers (strict match in the table).</summary>
internal readonly record struct KeyStroke(ConsoleKey Key, bool Alt, bool Ctrl, bool Shift);

/// <summary>
/// Custom bindings (<c>keybindings.json</c> next to settings):
/// command — notation ("Ctrl+Shift+S") or null (unbind).
/// </summary>
internal static class KeyBindings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultPath(string? settingsDir) =>
        Path.Combine(settingsDir ?? Directory.GetCurrentDirectory(), "keybindings.json");

    /// <summary>Example template (all commented out — defaults unchanged).</summary>
    public static void SeedExample(string path)
    {
        try
        {
            if (File.Exists(path))
                return;
            File.WriteAllText(path, """
                // TuiEdit keybindings: "Command": "key", null unbinds. Bad entries are ignored.
                // Notation: Ctrl/Alt/Shift + key (case-insensitive): "Ctrl+S", "Alt+/", "F9", "Ctrl+Shift+F3".
                // A printable key needs Ctrl or Alt (bare letters would break typing).
                // On conflict the later entry wins.
                {
                  // "Save": "Ctrl+S",
                  // "ToggleComment": "Alt+/",
                  // "GoToLine": null
                }
                """);
        }
        catch
        {
        }
    }

    /// <summary>Raw "command — notation/null" entries; broken file — empty (defaults).</summary>
    public static Dictionary<string, string?> Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }

    private static readonly Dictionary<string, ConsoleKey> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = ConsoleKey.Escape,
        ["escape"] = ConsoleKey.Escape,
        ["space"] = ConsoleKey.Spacebar,
        ["tab"] = ConsoleKey.Tab,
        ["enter"] = ConsoleKey.Enter,
        ["return"] = ConsoleKey.Enter,
        ["backspace"] = ConsoleKey.Backspace,
        ["del"] = ConsoleKey.Delete,
        ["delete"] = ConsoleKey.Delete,
        ["ins"] = ConsoleKey.Insert,
        ["insert"] = ConsoleKey.Insert,
        ["up"] = ConsoleKey.UpArrow,
        ["down"] = ConsoleKey.DownArrow,
        ["left"] = ConsoleKey.LeftArrow,
        ["right"] = ConsoleKey.RightArrow,
        ["home"] = ConsoleKey.Home,
        ["end"] = ConsoleKey.End,
        ["pgup"] = ConsoleKey.PageUp,
        ["pageup"] = ConsoleKey.PageUp,
        ["pgdn"] = ConsoleKey.PageDown,
        ["pagedown"] = ConsoleKey.PageDown,
        ["."] = ConsoleKey.OemPeriod,
        ["/"] = ConsoleKey.Oem2,
        ["-"] = ConsoleKey.OemMinus,
        [";"] = ConsoleKey.Oem1,
        ["'"] = ConsoleKey.Oem7,
        [","] = ConsoleKey.OemComma,
        ["`"] = ConsoleKey.Oem3,
        ["["] = ConsoleKey.Oem4,
        ["]"] = ConsoleKey.Oem6,
        ["\\"] = ConsoleKey.Oem5,
        ["="] = ConsoleKey.OemPlus,
    };

    /// <summary>Parse the notation; null — garbage, printable guard, or bare utility key.</summary>
    public static KeyStroke? Parse(string? notation)
    {
        if (string.IsNullOrWhiteSpace(notation))
            return null;
        string[] parts = notation.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;
        bool alt = false, ctrl = false, shift = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: return null;
            }
        }
        ConsoleKey key = ResolveKey(parts[^1]);
        if (key == ConsoleKey.NoName)
            return null;
        if (!ctrl && !alt && !IsBareAllowed(key))
            return null; // bare printable/utility key — typing protection
        return new KeyStroke(key, alt, ctrl, shift);
    }

    private static ConsoleKey ResolveKey(string token)
    {
        if (Aliases.TryGetValue(token, out ConsoleKey a))
            return a;
        if (token.Length == 1)
        {
            // Previously Enum.TryParse: "5" parses to a numeric value, not D5.
            char c = token[0];
            if (c is >= '0' and <= '9'
                && Enum.TryParse<ConsoleKey>("D" + c, out ConsoleKey digit))
                return digit;
            // TryParse, not Parse: non-ASCII letters (Ctrl+Å) must degrade to
            // NoName instead of aborting the whole overrides batch.
            if (char.IsLetter(c)
                && Enum.TryParse<ConsoleKey>(char.ToUpperInvariant(c).ToString(), out ConsoleKey letter))
                return letter;
        }
        if (Enum.TryParse<ConsoleKey>(token, ignoreCase: true, out ConsoleKey k))
            return k;
        return ConsoleKey.NoName;
    }

    /// <summary>Bare (without Ctrl/Alt) only non-printable keys are allowed.</summary>
    private static bool IsBareAllowed(ConsoleKey key) => key is
        >= ConsoleKey.F1 and <= ConsoleKey.F24
        or ConsoleKey.UpArrow or ConsoleKey.DownArrow
        or ConsoleKey.LeftArrow or ConsoleKey.RightArrow
        or ConsoleKey.Home or ConsoleKey.End
        or ConsoleKey.PageUp or ConsoleKey.PageDown;

    /// <summary>Back to string for menu hints ("^S", "Alt+/", "F9", "Ctrl+Shift+S").</summary>
    public static string Format(KeyStroke s)
    {
        string key = s.Key switch
        {
            >= ConsoleKey.F1 and <= ConsoleKey.F24 => s.Key.ToString(),
            ConsoleKey.UpArrow => "Up",
            ConsoleKey.DownArrow => "Down",
            ConsoleKey.LeftArrow => "Left",
            ConsoleKey.RightArrow => "Right",
            ConsoleKey.Home => "Home",
            ConsoleKey.End => "End",
            ConsoleKey.PageUp => "PageUp",
            ConsoleKey.PageDown => "PageDown",
            ConsoleKey.Spacebar => "Space",
            ConsoleKey.Tab => "Tab",
            ConsoleKey.Enter => "Enter",
            ConsoleKey.Escape => "Esc",
            ConsoleKey.Backspace => "Backspace",
            ConsoleKey.Delete => "Delete",
            ConsoleKey.Insert => "Insert",
            >= ConsoleKey.D0 and <= ConsoleKey.D9 => ((char)('0' + (s.Key - ConsoleKey.D0))).ToString(),
            _ => s.Key.ToString(), // letters — as-is ("S"); Oem keys are not mapped back
        };
        if (s is { Alt: false, Ctrl: true, Shift: false } && key.Length == 1 && char.IsLetter(key[0]))
            return "^" + key;
        string mods = string.Empty;
        if (s.Alt) mods += "Alt+";
        if (s.Ctrl) mods += "Ctrl+";
        if (s.Shift) mods += "Shift+";
        return mods + key;
    }
}
