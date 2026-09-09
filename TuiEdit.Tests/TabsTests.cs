using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Tabs: model, stateful switching, closing, keys.</summary>
[Trait("Category", "Integration")]
public sealed class TabsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _fileA;
    private readonly string _fileB;

    public TabsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_tabs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _fileA = Path.Combine(_dir, "a.txt");
        _fileB = Path.Combine(_dir, "b.txt");
        File.WriteAllLines(_fileA, new[] { "a1", "a2", "a3" });
        File.WriteAllLines(_fileB, new[] { "b1", "b2" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), "tui_tabcfg_" + Guid.NewGuid().ToString("N"), "s.json")));
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
    public void TabKeysMap()
    {
        Assert.Equal(EditorCommand.NextTab, KeyMap.Map(K('\t', ConsoleKey.Tab, ctrl: true)));
        Assert.Equal(EditorCommand.PrevTab, KeyMap.Map(new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, true)));
        // Main path is Ctrl+PgDn/PgUp (Ctrl+Tab is intercepted by Windows Terminal).
        Assert.Equal(EditorCommand.NextTab, KeyMap.Map(K('\0', ConsoleKey.PageDown, ctrl: true)));
        Assert.Equal(EditorCommand.PrevTab, KeyMap.Map(K('\0', ConsoleKey.PageUp, ctrl: true)));
        // Bare PgUp/PgDn still page.
        Assert.Equal(EditorCommand.PageDown, KeyMap.Map(K('\0', ConsoleKey.PageDown)));
        Assert.Equal(EditorCommand.PageUp, KeyMap.Map(K('\0', ConsoleKey.PageUp)));
        Assert.Equal(EditorCommand.NewTab, KeyMap.Map(K('\x14', ConsoleKey.T, ctrl: true)));
        Assert.Equal(EditorCommand.CloseTab, KeyMap.Map(K('\x17', ConsoleKey.W, ctrl: true)));
        Assert.Equal(EditorCommand.GoTabNumber, KeyMap.Map(K('1', ConsoleKey.D1, alt: true)));
        // Bare Tab still inserts.
        Assert.Equal(EditorCommand.InsertTab, KeyMap.Map(K('\t', ConsoleKey.Tab)));
    }

    [Fact]
    public void NewTabReusesCleanUntitled()
    {
        var ed = NewEditor();
        Assert.Equal(1, ed.TabCount);
        ed.NewTab();
        Assert.Equal(1, ed.TabCount);
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        Assert.Equal(2, ed.TabCount);
        Assert.Equal(1, ed.ActiveTab);
        Assert.Equal("", ActiveBuf(ed).GetLine(0));
    }

    [Fact]
    public void SwitchPreservesViewAndSelection()
    {
        var ed = NewEditor();
        ActiveBuf(ed).Open(_fileA);
        Set(ed, "_row", 2);
        Set(ed, "_col", 1);
        var sel = (TextSelection)Get(ed, "_sel")!;
        sel.Start(0, 0);
        ed.NewTab();
        ActiveBuf(ed).Open(_fileB);
        Assert.Equal(_fileB, ActiveBuf(ed).FilePath);
        ed.SwitchTab(0);
        Assert.Equal(_fileA, ActiveBuf(ed).FilePath);
        Assert.Equal(2, Get(ed, "_row"));
        Assert.Equal(1, Get(ed, "_col"));
        Assert.True(sel.HasSelection(2, 1));
        ed.SwitchTab(5); // wraps around
        Assert.Equal(1, ed.ActiveTab);
        ed.SwitchTab(-1); // last one
        Assert.Equal(1, ed.ActiveTab);
        ed.SwitchTab(-2); // wraps back
        Assert.Equal(0, ed.ActiveTab);
    }

    [Fact]
    public void TabTitle()
    {
        var loc = Loc.Load("en");
        var ed = NewEditor();
        Assert.Equal("Untitled.txt", ActiveBuf(ed) is not null ? new DocTab(ActiveBuf(ed)).TabTitle(loc) : "?");
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        Assert.Equal("Untitled.txt*", new DocTab(ActiveBuf(ed)).TabTitle(loc));
        ActiveBuf(ed).Open(_fileA);
        Assert.Equal("a.txt", new DocTab(ActiveBuf(ed)).TabTitle(loc));
    }

    [Fact]
    public void CloseCleanAndLast()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        Assert.Equal(2, ed.TabCount);
        ed.CloseTab();
        Assert.Equal(1, ed.TabCount);
        Assert.Equal(0, ed.ActiveTab);
        ed.CloseTabNow(); // last one — cleared, not closed
        Assert.Equal(1, ed.TabCount);
        Assert.Equal("", ActiveBuf(ed).GetLine(0));
    }

    [Fact]
    public void CloseModifiedAsks()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.CloseTab();
        Assert.NotNull(Get(ed, "_dialog"));
        Assert.Equal(1, ed.TabCount);
    }

    [Fact]
    public void GoTabNumberViaKeys()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        ActiveBuf(ed).InsertChar(0, 0, 'y');
        ed.NewTab();
        Assert.Equal(3, ed.TabCount);
        HandleKey(ed, K('1', ConsoleKey.D1, alt: true));
        Assert.Equal(0, ed.ActiveTab);
        HandleKey(ed, K('\0', ConsoleKey.PageDown, ctrl: true));
        Assert.Equal(1, ed.ActiveTab);
        HandleKey(ed, K('\0', ConsoleKey.PageUp, ctrl: true));
        Assert.Equal(0, ed.ActiveTab);
        HandleKey(ed, K('9', ConsoleKey.D9, alt: true)); // no such tab — staying put
        Assert.Equal(0, ed.ActiveTab);
    }

    [Theory]
    // Three tabs of 10 columns, screen 25: two fit.
    [InlineData(0, 0, 25, 0)]
    [InlineData(1, 0, 25, 0)]
    [InlineData(2, 0, 25, 1)]
    [InlineData(2, 1, 25, 1)]
    [InlineData(0, 1, 25, 0)] // backwards — window moves
    public void TabWindowScroll(int active, int left, int width, int want)
    {
        var widths = new List<int> { 10, 10, 10 };
        Assert.Equal(want, TuiEditor.TabWindowStart(widths, active, left, width));
    }

    [Fact]
    public void TabWindowScrollEdgeCases()
    {
        var widths = new List<int> { 10, 10, 10 };
        Assert.Equal(0, TuiEditor.TabWindowStart(widths, 0, 0, 100)); // everything fits
        Assert.Equal(0, TuiEditor.TabWindowStart(new List<int>(), 0, 0, 25));
    }

    [Fact]
    public void TabsPopupShape()
    {
        var loc = Loc.Load("en");
        var titles = Enumerable.Range(1, 12).Select(i => $"f{i}.txt").ToList();
        var m = ModalState.Tabs(loc, titles);
        Assert.Equal(ModalKind.Tabs, m.Kind);
        Assert.False(m.Danger);
        Assert.Equal(12, m.Buttons.Count);
        Assert.Equal('1', m.Buttons[0].Hotkey);
        Assert.Equal('0', m.Buttons[9].Hotkey);
        Assert.Equal('\0', m.Buttons[10].Hotkey);
        Assert.Equal(5, m.MaxVisibleButtons);
        Assert.StartsWith("[1]", m.Buttons[0].Label);
        Assert.Throws<ArgumentException>(() => ModalState.Tabs(loc, new List<string>()));
        // Hotkey and Enter select the tab.
        Assert.Equal(9, m.HandleKey(new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false)).Button);
    }

    [Fact]
    public void ListTabsPopupSwitches()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        ActiveBuf(ed).InsertChar(0, 0, 'y');
        HandleKey(ed, K('\x10', ConsoleKey.P, ctrl: true));
        Assert.NotNull(Get(ed, "_dialog"));
        // Pick the second button (tab 1) with Enter after Down arrow.
        HandleKey(ed, K('\0', ConsoleKey.DownArrow));
        HandleKey(ed, K('\0', ConsoleKey.Enter));
        Assert.Equal(1, ed.ActiveTab);
        Assert.Null(Get(ed, "_dialog"));
    }

    [Fact]
    public void AltZeroIsTenth()
    {
        Assert.Equal(EditorCommand.GoTabNumber, KeyMap.Map(K('0', ConsoleKey.D0, alt: true)));
        var ed = NewEditor();
        for (int i = 0; i < 10; i++)
        {
            ActiveBuf(ed).InsertChar(0, 0, (char)('a' + i));
            ed.NewTab();
        }
        Assert.Equal(11, ed.TabCount);
        HandleKey(ed, K('0', ConsoleKey.D0, alt: true));
        Assert.Equal(9, ed.ActiveTab);
    }

    [Fact]
    public void CtrlTAndCtrlWViaKeys()
    {
        var ed = NewEditor();
        HandleKey(ed, K('\x14', ConsoleKey.T, ctrl: true));
        Assert.Equal(1, ed.TabCount);
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        HandleKey(ed, K('\x14', ConsoleKey.T, ctrl: true));
        Assert.Equal(2, ed.TabCount);
        HandleKey(ed, K('\x17', ConsoleKey.W, ctrl: true));
        Assert.Equal(1, ed.TabCount);
    }

    private static string ModalLine(TuiEditor ed)
    {
        object dlg = Get(ed, "_dialog")!;
        object state = dlg.GetType()
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dlg)!;
        return ((ModalState)state).Lines[0];
    }

    [Fact]
    public void DiscardAllQuits()
    {
        // Two dirty tabs: "Don't save" twice — quitting, not cycling.
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        ActiveBuf(ed).InsertChar(0, 0, 'y');
        HandleKey(ed, K('\x11', ConsoleKey.Q, ctrl: true));
        Assert.NotNull(Get(ed, "_dialog"));
        Assert.Contains("Untitled.txt", ModalLine(ed)); // visible that we are prompting
        HandleKey(ed, K('n', ConsoleKey.N)); // "Don't save" for tab 2
        Assert.NotNull(Get(ed, "_dialog")); // modal for tab 1
        HandleKey(ed, K('n', ConsoleKey.N)); // "Don't save" for tab 1
        Assert.Null(Get(ed, "_dialog"));
        Assert.True((bool)Get(ed, "_quitRequested")!);
    }

    [Fact]
    public void DiscardClosesTab()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.NewTab();
        ed.SwitchTab(0);
        HandleKey(ed, K('\x17', ConsoleKey.W, ctrl: true));
        Assert.NotNull(Get(ed, "_dialog"));
        HandleKey(ed, K('n', ConsoleKey.N)); // "Don't save"
        Assert.Null(Get(ed, "_dialog"));
        Assert.Equal(1, ed.TabCount);
        Assert.Equal("", ActiveBuf(ed).GetLine(0));
    }
}
