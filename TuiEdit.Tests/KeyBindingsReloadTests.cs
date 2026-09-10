using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Editing keybindings.json outside the editor must take effect without a restart.
[Trait("Category", "Integration")]
public sealed class KeyBindingsReloadTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void ExternalEditReappliesWithoutRestart()
    {
        string kb = tmp.WriteFile("{}", "keybindings.json");
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        var ed = new TuiEditor(new TextBuffer(null), new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: table);
        ed.TrackKeyBindings(kb);

        Assert.Equal(EditorCommand.Save, table.Map(CtrlS()));

        File.WriteAllText(kb, """{"Save": null}""");
        File.SetLastWriteTimeUtc(kb, DateTime.UtcNow.AddSeconds(2)); // mtime granularity
        Reload(ed);

        Assert.Equal(EditorCommand.None, table.Map(CtrlS()));
    }

    [Fact]
    public void DeletedFileResetsToDefaults()
    {
        string kb = tmp.WriteFile("""{"Save": null}""", "keybindings.json");
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        var ed = new TuiEditor(new TextBuffer(null), new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: table);
        ed.TrackKeyBindings(kb);
        File.SetLastWriteTimeUtc(kb, DateTime.UtcNow.AddSeconds(2)); // mtime granularity
        Reload(ed); // picks up the override
        Assert.Equal(EditorCommand.None, table.Map(CtrlS()));

        File.Delete(kb);
        Reload(ed); // gone: Load yields empty, defaults restored
        Assert.Equal(EditorCommand.Save, table.Map(CtrlS()));
    }

    private static ConsoleKeyInfo CtrlS() =>
        new('\x13', ConsoleKey.S, false, false, true);

    private static void Reload(TuiEditor ed) =>
        typeof(TuiEditor).GetMethod("MaybeReloadKeyBindings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, []);
}
