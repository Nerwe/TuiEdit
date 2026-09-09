namespace TuiEdit;

/// <summary>
/// Converts <see cref="System.ConsoleKeyInfo"/> to <see cref="EditorCommand"/>.
/// </summary>
/// <remarks>
/// Ctrl+S reaches the app thanks to raw input mode
/// (<see cref="Terminal.TryEnableRawInput"/>): without it conhost swallows it
/// as the XOFF output pause (see microsoft/terminal#809).
/// Process-wide facade over <see cref="KeyBindingTable.Current"/>; editors should
/// prefer their own injected table. Defaults live in
/// <see cref="KeyBindingTable.DefaultRows"/> (order = priority); keybindings.json
/// compiles on top: replaces a command's keys, null unbinds.
/// </remarks>
internal static class KeyMap
{
    private static KeyBindingTable _current = new(KeyBindingTable.DefaultRows());

    /// <summary>
    /// Gets or sets the process-wide binding table (defaults plus keybindings.json overrides).
    /// </summary>
    public static KeyBindingTable Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Applies raw overrides (order of the file, last-wins; null unbinds).</summary>
    /// <param name="raw">Command name to key notation (or <see langword="null"/>).</param>
    public static void SetOverrides(Dictionary<string, string?> raw) => Current.ApplyOverrides(raw);

    /// <summary>Resets to defaults (used by tests).</summary>
    public static void ResetToDefaults() => Current = new KeyBindingTable(KeyBindingTable.DefaultRows());

    /// <summary>Menu hint for a command: null means no override (use the literal).</summary>
    /// <param name="cmd">One of the enumeration values that specifies the command.</param>
    public static string? HintFor(EditorCommand cmd) => Current.HintFor(cmd);

    /// <summary>Maps a key event to a command (falls back to InsertChar for text).</summary>
    /// <param name="key">The key event to map.</param>
    public static EditorCommand Map(ConsoleKeyInfo key) => Current.Map(key);
}
