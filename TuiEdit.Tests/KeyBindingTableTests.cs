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
}
