using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Сплит-вид: панели со своими вкладками, фокус, закрытие, клавиши.</summary>
public sealed class PanesTests
{
    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tui_panecfg_" + Guid.NewGuid().ToString("N"), "s.json")));
    }

    private static void Set(object o, string name, object? v) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, v);

    private static object? Get(object o, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    [Fact]
    public void PaneKeysMap()
    {
        Assert.Equal(EditorCommand.SplitPane, KeyMap.Map(K('s', ConsoleKey.S, alt: true)));
        Assert.Equal(EditorCommand.NextPane, KeyMap.Map(K('\0', ConsoleKey.F6)));
        Assert.Equal(EditorCommand.PrevPane,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F6, true, false, false)));
        Assert.Equal(EditorCommand.GoPaneNumber, KeyMap.Map(K('1', ConsoleKey.D1, ctrl: true)));
        Assert.Equal(EditorCommand.GoPaneNumber, KeyMap.Map(K('2', ConsoleKey.D2, ctrl: true)));
    }

    [Fact]
    public void PaneWidthsSplit()
    {
        Assert.Equal(new[] { 48, 48 }, TuiEditor.PaneWidths(96, 2));
        Assert.Equal(new[] { 32, 32, 32 }, TuiEditor.PaneWidths(96, 3));
        int[] w = TuiEditor.PaneWidths(95, 2);
        Assert.Equal(new[] { 48, 47 }, w); // остаток — левым
        Assert.Empty(TuiEditor.PaneWidths(96, 0));
        Assert.Equal(new[] { 96 }, TuiEditor.PaneWidths(96, 1));
    }

    [Fact]
    public void SplitAndFocus()
    {
        var ed = NewEditor();
        Assert.Equal(1, ed.PaneCount);
        Assert.Equal(0, ed.ActivePane);
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        Set(ed, "_row", 0);
        ed.SplitPane();
        Assert.Equal(2, ed.PaneCount);
        Assert.Equal(1, ed.ActivePane);
        Assert.Equal("", ActiveBuf(ed).GetLine(0)); // новая панель — пустая вкладка
        ed.SwitchPane(0);
        Assert.Equal(0, ed.ActivePane);
        Assert.Equal("x", ActiveBuf(ed).GetLine(0)); // состояние первой цело
        ed.SwitchPane(5); // по кругу
        Assert.Equal(1, ed.ActivePane);
    }

    [Fact]
    public void PanesKeepOwnTabs()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        Assert.Equal(2, ed.TabCount);
        ed.SplitPane();
        Assert.Equal(1, ed.TabCount); // у новой панели своя одна вкладка
        ed.SwitchPane(0);
        Assert.Equal(2, ed.TabCount);
        // Таб-клавиши работают по активной панели (стоим на вкладке 1 — завернёт на 0).
        Assert.Equal(1, ed.ActiveTab);
        HandleKey(ed, K('\t', ConsoleKey.Tab, ctrl: true));
        Assert.Equal(0, ed.ActiveTab);
        Assert.Equal(0, ed.ActivePane);
    }

    [Fact]
    public void CloseLastTabDropsPane()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.SplitPane();
        Assert.Equal(2, ed.PaneCount);
        ed.CloseTabNow(); // единственная вкладка панели — панель уходит
        Assert.Equal(1, ed.PaneCount);
        Assert.Equal(0, ed.ActivePane);
        Assert.Equal("x", ActiveBuf(ed).GetLine(0));
        ed.CloseTabNow(); // последняя панель — очищается
        Assert.Equal(1, ed.PaneCount);
        Assert.Equal("", ActiveBuf(ed).GetLine(0));
    }

    [Fact]
    public void QuitSeesDirtyInactivePane()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.SplitPane(); // активна чистая вторая
        HandleKey(ed, K('\x11', ConsoleKey.Q, ctrl: true)); // Ctrl+Q
        Assert.NotNull(Get(ed, "_dialog")); // спросили: грязь в первой панели
    }

    [Fact]
    public void SplitViaKeys()
    {
        var ed = NewEditor();
        HandleKey(ed, K('s', ConsoleKey.S, alt: true));
        Assert.Equal(2, ed.PaneCount);
        Assert.Equal(1, ed.ActivePane);
        HandleKey(ed, K('\0', ConsoleKey.F6));
        Assert.Equal(0, ed.ActivePane);
        HandleKey(ed, K('2', ConsoleKey.D2, ctrl: true));
        Assert.Equal(1, ed.ActivePane);
        HandleKey(ed, K('9', ConsoleKey.D9, ctrl: true)); // нет такой — стоим
        Assert.Equal(1, ed.ActivePane);
    }
}
