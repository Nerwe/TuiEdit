namespace TuiEdit;

/// <summary>Represents a mini-language command-line command (a plain structure after parsing).</summary>
internal abstract record CommandLineOp
{
    /// <summary>Sets a setting (a null value toggles it).</summary>
    public sealed record Set(string Key, string? Value) : CommandLineOp;

    /// <summary>Jumps to a line[:column].</summary>
    public sealed record Goto(int Line, int Col) : CommandLineOp;

    /// <summary>Finds a term further down the file.</summary>
    public sealed record Find(string Term) : CommandLineOp;

    public sealed record Save : CommandLineOp;

    public sealed record Quit : CommandLineOp;
}

/// <summary>
/// Provides the command-line mini-language: `set key [value]`, `goto line[:col]`,
/// `find term`, `save`, `quit`. Implements a pure parser; the editor applies the result.
/// </summary>
internal static class CommandLine
{
    /// <summary>Parses a line (returns null for empty/garbage input).</summary>
    public static CommandLineOp? Parse(string text)
    {
        string[] parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        switch (parts[0].ToLowerInvariant())
        {
            case "set" when parts.Length is 2 or 3:
                return new CommandLineOp.Set(parts[1].ToLowerInvariant(),
                    parts.Length == 3 ? parts[2] : null);
            case "goto" or "go" when parts.Length == 2:
                return TuiEditor.ParseGoTo(parts[1]) is (int line, int col)
                    ? new CommandLineOp.Goto(line, col)
                    : null;
            case "find" when parts.Length >= 2:
                return new CommandLineOp.Find(string.Join(' ', parts[1..]));
            case "save" or "w" when parts.Length == 1:
                return new CommandLineOp.Save();
            case "quit" or "q" or "exit" when parts.Length == 1:
                return new CommandLineOp.Quit();
            default:
                return null;
        }
    }

    /// <summary>
    /// Applies `set`: parses the key and value and mutates settings.
    /// Returns null on success, otherwise a localized error key.
    /// </summary>
    public static string? ApplySet(AppSettings settings, string key, string? value)
    {
        switch (key)
        {
            case "theme" when value is not null:
                if (!ThemeCatalog.Contains(settings, value))
                    return "cmdline.set.badvalue";
                settings.Theme = value;
                return null;
            case "lang" or "language" when value is not null:
                string? match = Loc.Supported.FirstOrDefault(l =>
                    string.Equals(l, value, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return "cmdline.set.badvalue";
                settings.Language = match;
                return null;
            case "ruler" when value is not null:
                if (!int.TryParse(value, out int ruler) || ruler < 0 || ruler > 1000)
                    return "cmdline.set.badvalue";
                settings.RulerColumn = ruler;
                return null;
            case "mouse" when value is not null:
                if (!Enum.TryParse(value, ignoreCase: true, out MouseLevel level)
                    || !Enum.IsDefined(level))
                    return "cmdline.set.badvalue";
                settings.Mouse = level;
                return null;
            case "numbers": return SetBool(value, () => settings.ShowLineNumbers, v => settings.ShowLineNumbers = v);
            case "wrap": return SetBool(value, () => settings.WordWrap, v => settings.WordWrap = v);
            case "whitespace": return SetBool(value, () => settings.ShowWhitespace, v => settings.ShowWhitespace = v);
            case "backup": return SetBool(value, () => settings.BackupOnSave, v => settings.BackupOnSave = v);
            case "guides": return SetBool(value, () => settings.ShowIndentGuides, v => settings.ShowIndentGuides = v);
            case "session": return SetBool(value, () => settings.RestoreSession, v => settings.RestoreSession = v);
            case "copyselect": return SetBool(value, () => settings.CopyOnSelect, v => settings.CopyOnSelect = v);
            case "pairs": return SetBool(value, () => settings.AutoPairs, v => settings.AutoPairs = v);
            case "gutter": return SetBool(value, () => settings.GitGutter, v => settings.GitGutter = v);
            case "theme" or "lang" or "language" or "ruler" or "mouse":
                return "cmdline.set.novalue";
            default:
                return "cmdline.set.unknown";
        }
    }

    /// <summary>Applies a bool option: toggles it when no value is given.</summary>
    private static string? SetBool(string? value, Func<bool> get, Action<bool> set)
    {
        if (value is null)
        {
            set(!get());
            return null;
        }
        bool? parsed = value.ToLowerInvariant() switch
        {
            "on" or "true" or "1" or "yes" => true,
            "off" or "false" or "0" or "no" => false,
            _ => null,
        };
        if (parsed is null)
            return "cmdline.set.badvalue";
        set(parsed.Value);
        return null;
    }
}
