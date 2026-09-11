using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// The per-editor table must not leak overrides into the process-wide one and vice versa.
public sealed class KeyBindingTableTests
{
    [Fact]
    public void OverridesAreIsolatedFromGlobalTable()
    {
        try
        {
            var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
            table.ApplyOverrides(new Dictionary<string, string?> { ["Save"] = "Ctrl+Shift+S" });

            Assert.Equal(EditorCommand.Save, table.Map(K('\x13', ConsoleKey.S, ctrl: true, shift: true)));
            Assert.Equal(EditorCommand.None, table.Map(K('\x13', ConsoleKey.S, ctrl: true)));

            Assert.Equal(EditorCommand.Save, KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true)));
        }
        finally
        {
            KeyMap.ResetToDefaults();
        }
    }

    [Fact]
    public void EditorRoutesKeysThroughInjectedTable()
    {
        KeyMap.ResetToDefaults();
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        table.ApplyOverrides(new Dictionary<string, string?> { ["ToggleSidebar"] = null });

        // Default table: Ctrl+B opens the sidebar.
        var edDefault = NewEditor();
        HandleKey(edDefault, K('\x02', ConsoleKey.B, ctrl: true));
        Assert.NotNull(Sidebar(edDefault));

        // Injected table with ToggleSidebar unbound: same key does nothing.
        var edCustom = NewEditor(table);
        HandleKey(edCustom, K('\x02', ConsoleKey.B, ctrl: true));
        Assert.Null(Sidebar(edCustom));
    }

    private static TuiEditor NewEditor(KeyBindingTable? keys = null) =>
        new(new TextBuffer(null), new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: keys);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static object? Sidebar(TuiEditor ed) =>
        typeof(TuiEditor).GetField("_sidebar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed);

    private static ConsoleKeyInfo K(char ch, ConsoleKey key, bool ctrl = false, bool shift = false) =>
        new(ch, key, shift, false, ctrl);

    private static string FirstLine(TuiEditor ed)
    {
        object? buf = typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed);
        var lines = (System.Collections.Generic.List<string>)buf!.GetType()
            .GetProperty("Lines")!.GetValue(buf)!;
        return lines[0];
    }

    private static void Undo(TuiEditor ed) =>
        HandleKey(ed, K('\x1a', ConsoleKey.Z, ctrl: true));

    [Fact]
    public void SpeedRunMergesUndoIntoOne()
    {
        int saved = TuiEditor.SpeedRunGapMs;
        TuiEditor.SpeedRunGapMs = 100000; // every gap counts as wire-speed
        try
        {
            var ed = NewEditor();
            foreach (char c in "abcdef")
                HandleKey(ed, K(c, (ConsoleKey)char.ToUpperInvariant(c)));
            Assert.Equal("abcdef", FirstLine(ed));
            Undo(ed);
            Assert.Equal("", FirstLine(ed)); // one undo removes the whole run
        }
        finally
        {
            TuiEditor.SpeedRunGapMs = saved;
        }
    }

    [Fact]
    public void SlowTypingKeepsSeparateUndos()
    {
        int saved = TuiEditor.SpeedRunGapMs;
        TuiEditor.SpeedRunGapMs = 0; // no gap counts as wire-speed
        try
        {
            var ed = NewEditor();
            HandleKey(ed, K('a', ConsoleKey.A));
            HandleKey(ed, K('b', ConsoleKey.B));
            Assert.Equal("ab", FirstLine(ed));
            Undo(ed);
            Assert.Equal("a", FirstLine(ed)); // only the last char undone
        }
        finally
        {
            TuiEditor.SpeedRunGapMs = saved;
        }
    }

    [Fact]
    public void SingleCharKeepsOwnUndo()
    {
        var ed = NewEditor();
        HandleKey(ed, K('a', ConsoleKey.A));
        Assert.Equal("a", FirstLine(ed));
        Undo(ed);
        Assert.Equal("", FirstLine(ed));
    }

    [Fact]
    public void RapidCharsAreNeverDropped()
    {
        var ed = NewEditor();
        HandleKey(ed, K('a', ConsoleKey.A));
        HandleKey(ed, K('b', ConsoleKey.B));
        Assert.Equal("ab", FirstLine(ed));
    }

    [Fact]
    public void AltGrPrintableInsertsInsteadOfNone()
    {
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        var altGr = new ConsoleKeyInfo('€', ConsoleKey.E, shift: false, alt: true, control: true);
        Assert.Equal(EditorCommand.InsertChar, table.Map(altGr));
    }

    [Fact]
    public void CtrlAltControlCharStaysNone()
    {
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        var ctrlAlt = new ConsoleKeyInfo('\x01', ConsoleKey.A, shift: false, alt: true, control: true);
        Assert.Equal(EditorCommand.None, table.Map(ctrlAlt));
    }
}
