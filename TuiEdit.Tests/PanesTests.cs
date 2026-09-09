using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Сплит-вид: панели со своими вкладками, фокус, закрытие, клавиши.</summary>
public sealed class PanesTests
{
    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static TuiEditor NewEditor(AppSettings? settings = null)
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, settings ?? new AppSettings(),
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
        Assert.Equal("", ActiveBuf(ed).GetLine(0));
        ed.SwitchPane(0);
        Assert.Equal(0, ed.ActivePane);
        Assert.Equal("x", ActiveBuf(ed).GetLine(0));
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
        Assert.Equal(1, ed.TabCount);
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
        HandleKey(ed, K('\x11', ConsoleKey.Q, ctrl: true));
        Assert.NotNull(Get(ed, "_dialog")); // спросили: грязь в первой панели
    }

    private static MouseInput Click(int x, int y) =>
        new(x, y, MouseAction.LeftPress, MouseButton.Left);

    private static MouseInput WheelDown(int x, int y) =>
        new(x, y, MouseAction.WheelDown);

    private static MouseInput DragTo(int x, int y) =>
        new(x, y, MouseAction.Move, MouseButton.Left);

    private static MouseInput Release(int x, int y) =>
        new(x, y, MouseAction.Move);

    private static List<string> ClipboardOf(TuiEditor ed) =>
        (List<string>)Get(ed, "_clipboard")!;

    private static TextSelection SelOf(TuiEditor ed) =>
        (TextSelection)Get(ed, "_sel")!;

    [Fact]
    public void EditorLayoutSingleSource()
    {
        EditorLayout l = EditorLayout.Compute(80, 24, 0, 2, false);
        Assert.Equal([0, 40], l.PaneXs);
        Assert.Equal([40, 40], l.PaneWs);
        Assert.Equal(0, l.TabH);
        Assert.Equal(1, l.Y0);
        Assert.Equal(22, l.TextHeight);
        Assert.Equal(0, l.PaneAt(0));
        Assert.Equal(0, l.PaneAt(39));
        Assert.Equal(1, l.PaneAt(40));
        Assert.Equal(-1, l.PaneAt(-1));
        Assert.Equal(-1, l.PaneAt(80));
        EditorLayout s = EditorLayout.Compute(80, 24, 24, 1, true);
        Assert.Equal([24], s.PaneXs);
        Assert.Equal(1, s.TabH);
        Assert.Equal(2, s.Y0);
        Assert.Equal(-1, s.PaneAt(10)); // сайдбар
    }

    [Fact]
    public void TabHitMirrorsDrawTabs()
    {
        Assert.Equal(0, TuiEditor.TabHit(["aaa", "bb"], 1, 0, 80, 2));
        Assert.Equal(1, TuiEditor.TabHit(["aaa", "bb"], 1, 0, 80, 6));
        Assert.Null(TuiEditor.TabHit(["aaa", "bb"], 1, 0, 80, 79));
        Assert.Null(TuiEditor.TabHit(["only"], 0, 0, 80, 2));
        Assert.Null(TuiEditor.TabHit(["aaa", "bb"], 1, 0, 80, -1));
    }

    [Fact]
    public void ClickFocusesPane()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.SplitPane();
        Assert.Equal(1, ed.ActivePane);
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(10, 5), 80, 24);
            Assert.Equal(0, ed.ActivePane);
            Assert.Equal("x", ActiveBuf(ed).GetLine(0));
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void WheelOverGutterScrolls()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertText(0, 0, "a\nb\n");
        Assert.Equal(3, ActiveBuf(ed).Count);
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(WheelDown(0, 5), 80, 24); // гуттер, не текст
            Assert.Equal(2, Get(ed, "_row"));
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void TabClickSwitches()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x'); // грязная — NewTab не откажет
        ed.NewTab();
        Assert.Equal(2, ed.TabCount);
        Assert.Equal(1, ed.ActiveTab);
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(0, 1), 80, 24); // первая вкладка
            Assert.Equal(0, ed.ActiveTab);
            ed.HandleMouseAt(Click(79, 1), 80, 24); // мимо спанов — стоим
            Assert.Equal(0, ed.ActiveTab);
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void ModalBackdropClickCancels()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertChar(0, 0, 'x');
        ed.CloseTab(); // модалка «несохранённые изменения»
        Assert.NotNull(Get(ed, "_dialog"));
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(79, 23), 80, 24); // мимо бокса — как Esc
            Assert.Null(Get(ed, "_dialog"));
            Assert.Equal("x", ActiveBuf(ed).GetLine(0)); // вкладка жива, выхода нет
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void DragSelectsAndCopiesOnRelease()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertText(0, 0, "aaa\nbbb\nccc");
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(8, 1), 80, 24);
            Assert.False(SelOf(ed).Active); // press сам выделения не создаёт
            ed.HandleMouseAt(DragTo(10, 3), 80, 24);
            Assert.True(SelOf(ed).HasSelection(2, 2));
            ed.HandleMouseAt(Release(10, 3), 80, 24);
            Assert.Equal(["aaa", "bbb", "cc"], ClipboardOf(ed));
            Assert.False(SelOf(ed).Active); // копия ушла — гасим
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void DragKeepsSelectionWithoutCopyOnSelect()
    {
        var ed = NewEditor(new AppSettings { CopyOnSelect = false });
        ActiveBuf(ed).InsertText(0, 0, "aaa\nbbb\nccc");
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(8, 1), 80, 24);
            ed.HandleMouseAt(DragTo(10, 3), 80, 24);
            ed.HandleMouseAt(Release(10, 3), 80, 24);
            Assert.True(SelOf(ed).HasSelection(2, 2)); // осталась для клавиатуры
            Assert.Empty(ClipboardOf(ed));
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void RightClickCopiesSelection()
    {
        var ed = NewEditor(new AppSettings { CopyOnSelect = false });
        ActiveBuf(ed).InsertText(0, 0, "aaa\nbbb\nccc");
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(8, 1), 80, 24);
            ed.HandleMouseAt(DragTo(10, 3), 80, 24);
            ed.HandleMouseAt(Release(10, 3), 80, 24);
            ed.HandleMouseAt(new MouseInput(10, 3, MouseAction.RightPress, MouseButton.Right), 80, 24);
            Assert.Equal(["aaa", "bbb", "cc"], ClipboardOf(ed));
            Assert.False(SelOf(ed).Active);
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
    }

    [Fact]
    public void ClickWithoutDragLeavesNoSelection()
    {
        var ed = NewEditor();
        ActiveBuf(ed).InsertText(0, 0, "aaa\nbbb\nccc");
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            ed.HandleMouseAt(Click(8, 1), 80, 24);
            ed.HandleMouseAt(Release(8, 1), 80, 24); // сразу отпустили — клик
            Assert.False(SelOf(ed).Active);
            Assert.Empty(ClipboardOf(ed));
        }
        finally { InputReader.MouseLevel = MouseLevel.Off; }
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
