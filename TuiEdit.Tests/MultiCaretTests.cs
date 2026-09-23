using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Multi-cursor: model, bindings, fan-out edits, single undo, Esc.</summary>
public sealed class MultiCaretTests
{
    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), "tui_caretcfg_" + Guid.NewGuid().ToString("N"), "s.json")));
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

    private static MultiCaret Carets(TuiEditor ed)
    {
        var docs = (System.Collections.IList)typeof(TuiEditor).GetProperty("_docs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        dynamic tab = docs[0]!;
        return tab.Carets;
    }

    [Fact]
    public void ModelAddDedupsAndOrders()
    {
        var m = new MultiCaret();
        Assert.True(m.Add(2, 5, 0, 0));
        Assert.False(m.Add(2, 5, 0, 0)); // duplicate
        Assert.False(m.Add(0, 0, 0, 0)); // primary
        m.Add(0, 3, 9, 9);
        Assert.Equal([(0, 3), (2, 5)], m.Ordered.ToList());
        m.Clear();
        Assert.False(m.HasMultiple);
    }

    [Fact]
    public void BindingsMap()
    {
        Assert.Equal(EditorCommand.CaretAddNext, KeyMap.Map(K('d', ConsoleKey.D, alt: true)));
        Assert.Equal(EditorCommand.CaretAddBelow,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false)));
        Assert.Equal(EditorCommand.CaretAddAbove,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, true, true, false)));
    }

    [Fact]
    public void RepeatedAddBelowGrowsEveryPress()
    {
        var ed = NewEditor("a1", "b2", "c3", "d4", "e5");
        Set(ed, "_row", 0);
        Set(ed, "_col", 1);
        var down = new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false);
        HandleKey(ed, down);
        HandleKey(ed, down);
        HandleKey(ed, down);
        var ordered = Carets(ed).Ordered.ToList();
        Assert.Equal([(1, 1), (2, 1), (3, 1)], ordered);
        HandleKey(ed, K('X', ConsoleKey.X));
        var buf = ActiveBuf(ed);
        Assert.Equal("aX1", buf.GetLine(0));
        Assert.Equal("bX2", buf.GetLine(1));
        Assert.Equal("cX3", buf.GetLine(2));
        Assert.Equal("dX4", buf.GetLine(3));
        Assert.Equal("e5", buf.GetLine(4));
        buf.Undo();
        Assert.Equal("a1", buf.GetLine(0));
        Assert.Equal("b2", buf.GetLine(1));
        Assert.Equal("d4", buf.GetLine(3));
    }

    [Fact]
    public void AddUpAndDownGrowBothWays()
    {
        var ed = NewEditor("a1", "b2", "c3", "d4", "e5");
        Set(ed, "_row", 2);
        Set(ed, "_col", 0);
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false));
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false));
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, true, true, false));
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, true, true, false));
        var ordered = Carets(ed).Ordered.ToList();
        Assert.Equal([(0, 0), (1, 0), (3, 0), (4, 0)], ordered);
    }

    [Fact]
    public void AddBelowAndTypeFansOutWithSingleUndo()
    {
        var ed = NewEditor("aaa", "bbb", "ccc");
        Set(ed, "_row", 0);
        Set(ed, "_col", 3);
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false)); // Alt+Shift+Down
        Assert.True(Carets(ed).HasMultiple);
        HandleKey(ed, K('X', ConsoleKey.X)); // printable -> InsertChar
        var buf = ActiveBuf(ed);
        Assert.Equal("aaaX", buf.GetLine(0));
        Assert.Equal("bbbX", buf.GetLine(1));
        Assert.Equal("ccc", buf.GetLine(2));
        buf.Undo(); // one step reverts both carets
        Assert.Equal("aaa", buf.GetLine(0));
        Assert.Equal("bbb", buf.GetLine(1));
    }

    [Fact]
    public void AddNextOccurrence()
    {
        var ed = NewEditor("foo bar", "baz foo", "qux");
        Set(ed, "_row", 0);
        Set(ed, "_col", 1); // inside first "foo"
        HandleKey(ed, K('d', ConsoleKey.D, alt: true));
        var ordered = Carets(ed).Ordered.ToList();
        Assert.Contains((1, 4), ordered); // second "foo"
    }

    [Fact]
    public void AddNextWithNoWordMessages()
    {
        var ed = NewEditor("   ", "x");
        Set(ed, "_row", 0);
        Set(ed, "_col", 0);
        HandleKey(ed, K('d', ConsoleKey.D, alt: true));
        Assert.False(Carets(ed).HasMultiple);
    }

    [Fact]
    public void EscClearsExtraCarets()
    {
        var ed = NewEditor("aaa", "bbb");
        Set(ed, "_row", 0);
        Set(ed, "_col", 0);
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false));
        Assert.True(Carets(ed).HasMultiple);
        HandleKey(ed, new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.False(Carets(ed).HasMultiple);
    }

    [Fact]
    public void ArrowsMoveAllCarets()
    {
        var ed = NewEditor("aaa", "bbb", "ccc");
        Set(ed, "_row", 0);
        Set(ed, "_col", 1);
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false));
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal(2, Get(ed, "_col"));
        Assert.Contains((1, 2), Carets(ed).Ordered.ToList());
    }

    [Fact]
    public void BackspaceFansOut()
    {
        var ed = NewEditor("axb", "ayb");
        Set(ed, "_row", 0);
        Set(ed, "_col", 2);
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, true, true, false));
        // extra caret lands at (1,2); delete 'x' and 'y' in one step:
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.Backspace, false, false, false));
        var buf = ActiveBuf(ed);
        Assert.Equal("ab", buf.GetLine(0));
        Assert.Equal("ab", buf.GetLine(1));
        buf.Undo();
        Assert.Equal("axb", buf.GetLine(0));
        Assert.Equal("ayb", buf.GetLine(1));
    }
}
