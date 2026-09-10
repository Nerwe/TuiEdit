using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>File panel: model, Ctrl+B, focus and opening (fixed width).</summary>
[Trait("Category", "Integration")]
public sealed class SidebarTests : IDisposable
{
    private readonly string _root;

    public SidebarTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tui_sb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "y");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    [Fact]
    public void WidthFixed()
    {
        Assert.Equal(24, SidebarState.Width);
    }

    [Fact]
    public void SidebarClearsTabRow()
    {
        // The tab row above the panel must not leak stale text.
        var ed = NewEditor();
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        var scr = (Screen)typeof(TuiEditor)
            .GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        scr.Resize(96, 28);
        var theme = Themes.Get("dark");
        scr.Text(0, 1, new string('X', 96), theme.EditorFg, theme.EditorBg);
        typeof(TuiEditor).GetMethod("DrawSidebar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [2, 26]);
        for (int x = 0; x < 23; x++)
            Assert.Equal(' ', CellAt(scr, x, 1));
        Assert.Equal('│', CellAt(scr, 23, 1));
    }

    private static char CellAt(Screen scr, int x, int y)
    {
        var cur = (Array)typeof(Screen).GetField("_cur", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scr)!;
        object? cell = cur.GetValue(x, y);
        return (char)cell!.GetType().GetProperty("Ch")!.GetValue(cell)!;
    }

    [Fact]
    public void ClassifyKinds()
    {
        string f = Path.Combine(_root, "a.txt");
        var file = SidebarState.Classify(f, isDir: false);
        Assert.False(file.IsDir);
        Assert.False(file.IsHidden);
        Assert.False(file.IsExe);

        string dot = Path.Combine(_root, ".env");
        File.WriteAllText(dot, "x");
        var hidden = SidebarState.Classify(dot, isDir: false);
        Assert.True(hidden.IsHidden);
        Assert.False(hidden.IsExe);

        string bat = Path.Combine(_root, "run.BAT");
        File.WriteAllText(bat, "x");
        var exe = SidebarState.Classify(bat, isDir: false);
        Assert.True(exe.IsExe);
        Assert.False(exe.IsHidden);

        var dir = SidebarState.Classify(Path.Combine(_root, "sub"), isDir: true);
        Assert.True(dir.IsDir);
        Assert.False(dir.IsExe);
    }

    [Fact]
    public void EntryColors()
    {
        foreach (Theme theme in new[] { Themes.Get("dark"), Themes.Get("light") })
        {
            Assert.Equal(theme.PickerUpFg, TuiEditor.EntryFg(theme, new SidebarEntry("..", true)));
            Assert.Equal(theme.PickerHiddenFg, TuiEditor.EntryFg(theme, new SidebarEntry(".env", false, IsHidden: true)));
            Assert.Equal(theme.PickerExeFg, TuiEditor.EntryFg(theme, new SidebarEntry("run.bat", false, IsExe: true)));
            Assert.Equal(theme.PickerDirFg, TuiEditor.EntryFg(theme, new SidebarEntry("sub", true)));
            Assert.Equal(theme.EditorFg, TuiEditor.EntryFg(theme, new SidebarEntry("a.txt", false)));
            // Hiddenness outranks type.
            Assert.Equal(theme.PickerHiddenFg, TuiEditor.EntryFg(theme, new SidebarEntry(".h", true, IsHidden: true)));
            // Type colors are distinguishable from each other.
            Assert.NotEqual(theme.PickerDirFg, theme.PickerExeFg);
            Assert.NotEqual(theme.PickerDirFg, theme.EditorFg);
        }
    }

    [Fact]
    public void ModelListsTreeDirsFirst()
    {
        var sb = new SidebarState(_root);
        Assert.Equal(Path.GetFullPath(_root), sb.CurrentDir);
        // No ".." anymore: sub, a.txt, b.txt at depth 0.
        Assert.Equal(["sub", "a.txt", "b.txt"], sb.Rows.Select(r => r.node.Name).ToList());
        Assert.All(sb.Rows, r => Assert.Equal(0, r.depth));
        Assert.True(sb.Rows[0].node.IsDir);
    }

    [Fact]
    public void ModelToggleExpandCollapse()
    {
        File.WriteAllText(Path.Combine(_root, "sub", "inner.txt"), "x");
        var sb = new SidebarState(_root);

        // Enter on a file is false (opens the editor).
        while (!sb.Rows[sb.Selected].node.Name.Equals("a.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        Assert.False(sb.EnterSelected());
        Assert.EndsWith("a.txt", sb.SelectedPath);

        // Enter on a folder expands in place (root stays).
        var sb2 = new SidebarState(_root);
        Assert.True(sb2.EnterSelected()); // sub is first
        Assert.Equal(Path.GetFullPath(_root), sb2.CurrentDir);
        Assert.Equal(["sub", "inner.txt", "a.txt", "b.txt"],
            sb2.Rows.Select(r => r.node.Name).ToList());
        Assert.Equal(1, sb2.Rows[1].depth);

        // Enter again collapses.
        Assert.True(sb2.EnterSelected());
        Assert.Equal(["sub", "a.txt", "b.txt"], sb2.Rows.Select(r => r.node.Name).ToList());
    }

    [Fact]
    public void ModelCollapseOrParent()
    {
        File.WriteAllText(Path.Combine(_root, "sub", "inner.txt"), "x");
        var sb = new SidebarState(_root);
        sb.EnterSelected(); // expand sub
        sb.MoveHighlight(1, 10); // inner.txt
        sb.CollapseOrParent(); // jump to the parent folder
        Assert.Equal("sub", sb.Rows[sb.Selected].node.Name);
        sb.CollapseOrParent(); // collapse it
        Assert.Equal(["sub", "a.txt", "b.txt"], sb.Rows.Select(r => r.node.Name).ToList());
    }

    [Fact]
    public void ModelEnterAndScroll()
    {
        var sb = new SidebarState(_root);
        // Scroll with a window of 2 over 3 rows.
        sb.MoveHighlight(2, 2);
        Assert.Equal(2, sb.Selected);
        Assert.Equal(1, sb.Top);
        sb.MoveHighlight(-2, 2);
        Assert.Equal(0, sb.Selected);
        Assert.Equal(0, sb.Top);
    }

    [Fact]
    public void ModelBadDirSilent()
    {
        var sb = new SidebarState(Path.Combine(_root, "ghost", "\0??"));
        Assert.Empty(sb.Rows);
        Assert.Null(sb.SelectedPath);
        Assert.False(sb.SelectedIsDir);
        Assert.False(sb.EnterSelected());
    }

    [Fact]
    public void CtrlBMaps()
    {
        Assert.Equal(EditorCommand.ToggleSidebar, KeyMap.Map(K('\x02', ConsoleKey.B, ctrl: true)));
    }

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), "tui_sbcfg_" + Guid.NewGuid().ToString("N"), "s.json")));
    }

    private static object? Field(object o, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    [Fact]
    public void ToggleCycle()
    {
        var ed = NewEditor();
        Assert.Null(Field(ed, "_sidebar"));
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // open + focus
        Assert.NotNull(Field(ed, "_sidebar"));
        Assert.True((bool)Field(ed, "_sidebarFocus")!);
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // focused — close
        Assert.Null(Field(ed, "_sidebar"));
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        HandleKey(ed, K('\x1B', ConsoleKey.Escape)); // Esc — focus to text, panel stays alive
        Assert.NotNull(Field(ed, "_sidebar"));
        Assert.False((bool)Field(ed, "_sidebarFocus")!);
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // unfocused — restore focus
        Assert.True((bool)Field(ed, "_sidebarFocus")!);
    }

    [Fact]
    public void EnterOpensFile()
    {
        var ed = NewEditor();
        // Root the panel at the test folder, then open b.txt with Enter.
        ed.OpenSidebarRoot(_root);
        var sb = (SidebarState)Field(ed, "_sidebar")!;
        while (!sb.Rows[sb.Selected].node.Name.Equals("b.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        HandleKey(ed, K('\0', ConsoleKey.Enter));
        // _buf is the active tab property (not a field).
        var buf = (TextBuffer)typeof(TuiEditor)
            .GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        Assert.Equal(Path.Combine(_root, "b.txt"), buf.FilePath);
        Assert.False((bool)Field(ed, "_sidebarFocus")!);
        Assert.NotNull(Field(ed, "_sidebar")); // panel stayed open
    }

    [Fact]
    public void ArmedDeleteFlow()
    {
        string target = Path.Combine(_root, "kill.txt");
        File.WriteAllText(target, "x");
        var ed = NewEditor();
        ed.OpenSidebarRoot(_root);
        var sb = (SidebarState)Field(ed, "_sidebar")!;
        while (!sb.Rows[sb.Selected].node.Name.Equals("kill.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        HandleKey(ed, K('\0', ConsoleKey.Delete)); // arm — nothing deleted yet
        Assert.True(File.Exists(target));
        HandleKey(ed, K('\0', ConsoleKey.Delete)); // confirm
        Assert.False(File.Exists(target));

        // Another key disarms instead of deleting.
        string target2 = Path.Combine(_root, "spare.txt");
        File.WriteAllText(target2, "x");
        sb.Refresh(); // external creation appears on refresh (a keypress does this live)
        while (!sb.Rows[sb.Selected].node.Name.Equals("spare.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        HandleKey(ed, K('\0', ConsoleKey.Delete)); // arm on spare.txt
        HandleKey(ed, K('\0', ConsoleKey.UpArrow)); // disarm + move to b.txt
        HandleKey(ed, K('\0', ConsoleKey.Delete)); // arms b.txt instead of deleting spare
        Assert.True(File.Exists(target2));
        Assert.True(File.Exists(Path.Combine(_root, "b.txt")));
    }

    [Fact]
    public void TreeFrameGolden()
    {
        // Fixed dir name: the panel header shows the root basename (no GUIDs in goldens).
        string dir = Path.Combine(Path.GetTempPath(), "tui_sb_golden");
        try
        {
            try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "sub", "inner.txt"), "x");
            var ed = NewEditor();
            ed.OpenSidebarRoot(dir);
            var sb = (SidebarState)Field(ed, "_sidebar")!;
            sb.EnterSelected(); // expand sub
            var scr = (Screen)typeof(TuiEditor)
                .GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
            scr.Resize(40, 12);
            typeof(TuiEditor).GetMethod("DrawSidebar", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ed, [2, 9]);
            Golden.AssertMatch("sidebar.en.txt", scr);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NavigateShowsPreviewInSameTab()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "AAA");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "BBB");
        var ed = NewEditor();
        ed.OpenSidebarRoot(_root);
        HandleKey(ed, K('\0', ConsoleKey.DownArrow)); // a.txt: preview
        Assert.Equal(1, ed.TabCount);
        Assert.EndsWith("a.txt", ActiveBufPath(ed));
        Assert.Equal("AAA", ActiveBuf(ed).GetLine(0));
        HandleKey(ed, K('\0', ConsoleKey.DownArrow)); // b.txt: same tab reused
        Assert.Equal(1, ed.TabCount);
        Assert.EndsWith("b.txt", ActiveBufPath(ed));
        Assert.Equal("BBB", ActiveBuf(ed).GetLine(0));
    }

    [Fact]
    public void EnterPinsPreview()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "AAA");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "BBB");
        var ed = NewEditor();
        ed.OpenSidebarRoot(_root);
        HandleKey(ed, K('\0', ConsoleKey.DownArrow));
        HandleKey(ed, K('\0', ConsoleKey.DownArrow)); // preview b.txt
        Assert.EndsWith("b.txt", ActiveBufPath(ed));
        HandleKey(ed, K('\0', ConsoleKey.Enter)); // pin
        Assert.False((bool)Field(ed, "_sidebarFocus")!);
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // back to the panel
        HandleKey(ed, K('\0', ConsoleKey.UpArrow)); // preview a.txt elsewhere
        Assert.Equal(2, ed.TabCount); // pinned b.txt kept
        Assert.EndsWith("a.txt", ActiveBufPath(ed));
    }

    [Fact]
    public void DirtyPreviewPinsOnNavigate()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "AAA");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "BBB");
        var ed = NewEditor();
        ed.OpenSidebarRoot(_root);
        HandleKey(ed, K('\0', ConsoleKey.DownArrow));
        HandleKey(ed, K('\0', ConsoleKey.DownArrow)); // preview b.txt
        HandleKey(ed, K('\x1B', ConsoleKey.Escape)); // focus to text
        HandleKey(ed, K('X', ConsoleKey.X)); // dirty the preview
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // back to the panel
        HandleKey(ed, K('\0', ConsoleKey.UpArrow)); // preview a.txt
        Assert.Equal(2, ed.TabCount); // edited b.txt pinned, not replaced
        ed.SwitchTab(0);
        Assert.StartsWith("XBBB", ActiveBuf(ed).GetLine(0));
    }

    [Fact]
    public void LargeFileSkipsPreview()
    {
        string big = Path.Combine(_root, "big.bin");
        using (var fs = File.Create(big))
            fs.SetLength(TuiEditor.LargeFileBytes + 1);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "AAA");
        var ed = NewEditor();
        ed.OpenSidebarRoot(_root);
        var sb = (SidebarState)Field(ed, "_sidebar")!;
        while (!sb.Rows[sb.Selected].node.Name.Equals("b.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        HandleKey(ed, K('\0', ConsoleKey.DownArrow)); // lands on big.bin: skipped
        Assert.Equal(1, ed.TabCount); // startup tab untouched
        Assert.Null(ActiveBuf(ed).FilePath);
    }

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static string? ActiveBufPath(TuiEditor ed) => ActiveBuf(ed).FilePath;

    [Fact]
    public void TypingSwallowedInFocus()
    {
        var ed = NewEditor();
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        var buf = (TextBuffer)typeof(TuiEditor)
            .GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        int before = buf.Count;
        HandleKey(ed, K('x', ConsoleKey.X));
        Assert.Equal(before, buf.Count); // typing with panel focus is swallowed
    }
}
