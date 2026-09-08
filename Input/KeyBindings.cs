using System.Text.Json;

namespace TuiEdit;

/// <summary>Комбинация: клавиша + модификаторы (строгое совпадение в таблице).</summary>
internal readonly record struct KeyStroke(ConsoleKey Key, bool Alt, bool Ctrl, bool Shift);

/// <summary>
/// Пользовательские биндинги (<c>keybindings.json</c> рядом с настройками):
/// команда — нотация («Ctrl+Shift+S») или null (отвязать).
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

    /// <summary>Пример-шаблон (всё закомментировано — дефолты не меняет).</summary>
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

    /// <summary>Сырые записи «команда — нотация/null»; битый файл — пусто (дефолты).</summary>
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
        ["esc"] = ConsoleKey.Escape, ["escape"] = ConsoleKey.Escape,
        ["space"] = ConsoleKey.Spacebar, ["tab"] = ConsoleKey.Tab,
        ["enter"] = ConsoleKey.Enter, ["return"] = ConsoleKey.Enter,
        ["backspace"] = ConsoleKey.Backspace, ["del"] = ConsoleKey.Delete, ["delete"] = ConsoleKey.Delete,
        ["ins"] = ConsoleKey.Insert, ["insert"] = ConsoleKey.Insert,
        ["up"] = ConsoleKey.UpArrow, ["down"] = ConsoleKey.DownArrow,
        ["left"] = ConsoleKey.LeftArrow, ["right"] = ConsoleKey.RightArrow,
        ["home"] = ConsoleKey.Home, ["end"] = ConsoleKey.End,
        ["pgup"] = ConsoleKey.PageUp, ["pageup"] = ConsoleKey.PageUp,
        ["pgdn"] = ConsoleKey.PageDown, ["pagedown"] = ConsoleKey.PageDown,
        ["."] = ConsoleKey.OemPeriod, ["/"] = ConsoleKey.Oem2, ["-"] = ConsoleKey.OemMinus,
        [";"] = ConsoleKey.Oem1, ["'"] = ConsoleKey.Oem7, [","] = ConsoleKey.OemComma,
        ["`"] = ConsoleKey.Oem3, ["["] = ConsoleKey.Oem4, ["]"] = ConsoleKey.Oem6,
        ["\\"] = ConsoleKey.Oem5, ["="] = ConsoleKey.OemPlus,
    };

    /// <summary>Разобрать нотацию; null — мусор, охрана печати или голая служебная.</summary>
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
            return null; // голая печатаемая/слуюжебная — защита набора
        return new KeyStroke(key, alt, ctrl, shift);
    }

    private static ConsoleKey ResolveKey(string token)
    {
        if (Aliases.TryGetValue(token, out ConsoleKey a))
            return a;
        if (token.Length == 1)
        {
            // Раньше Enum.TryParse: "5" парсится в числовое значение, а не D5.
            char c = token[0];
            if (c is >= '0' and <= '9')
                return Enum.Parse<ConsoleKey>("D" + c);
            if (char.IsLetter(c))
                return Enum.Parse<ConsoleKey>(char.ToUpperInvariant(c).ToString());
        }
        if (Enum.TryParse<ConsoleKey>(token, ignoreCase: true, out ConsoleKey k))
            return k;
        return ConsoleKey.NoName;
    }

    /// <summary>Голыми (без Ctrl/Alt) разрешены только непечатаемые.</summary>
    private static bool IsBareAllowed(ConsoleKey key) => key is
        >= ConsoleKey.F1 and <= ConsoleKey.F24
        or ConsoleKey.UpArrow or ConsoleKey.DownArrow
        or ConsoleKey.LeftArrow or ConsoleKey.RightArrow
        or ConsoleKey.Home or ConsoleKey.End
        or ConsoleKey.PageUp or ConsoleKey.PageDown;

    /// <summary>Обратно в строку для подсказок меню («^S», «Alt+/», «F9», «Ctrl+Shift+S»).</summary>
    public static string Format(KeyStroke s)
    {
        string key = s.Key switch
        {
            >= ConsoleKey.F1 and <= ConsoleKey.F24 => s.Key.ToString(),
            ConsoleKey.UpArrow => "Up", ConsoleKey.DownArrow => "Down",
            ConsoleKey.LeftArrow => "Left", ConsoleKey.RightArrow => "Right",
            ConsoleKey.Home => "Home", ConsoleKey.End => "End",
            ConsoleKey.PageUp => "PageUp", ConsoleKey.PageDown => "PageDown",
            ConsoleKey.Spacebar => "Space", ConsoleKey.Tab => "Tab",
            ConsoleKey.Enter => "Enter", ConsoleKey.Escape => "Esc",
            ConsoleKey.Backspace => "Backspace", ConsoleKey.Delete => "Delete",
            ConsoleKey.Insert => "Insert",
            >= ConsoleKey.D0 and <= ConsoleKey.D9 => ((char)('0' + (s.Key - ConsoleKey.D0))).ToString(),
            _ => s.Key.ToString(), // буквы — как есть («S»); Oem обратно не маппим
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
