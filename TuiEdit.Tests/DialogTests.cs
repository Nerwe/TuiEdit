using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Единый механизм диалогов (бывший сьют Dialogs).</summary>
public sealed class DialogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cfgDir;
    private readonly Screen _scr;
    private readonly Theme _theme;
    private readonly Loc _loc;

    public DialogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_dlg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        _cfgDir = Path.Combine(Path.GetTempPath(), "tui_dlgcfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cfgDir);
        _scr = new Screen();
        _scr.Resize(96, 28);
        _theme = Themes.Get("dark");
        _loc = Loc.Load("ru");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        try { Directory.Delete(_cfgDir, true); } catch { }
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static char CellAt(Screen scr, int x, int y)
    {
        var cur = (Array)typeof(Screen).GetField("_cur", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scr)!;
        object? cell = cur.GetValue(x, y);
        return (char)cell!.GetType().GetProperty("Ch")!.GetValue(cell)!;
    }

    [Fact]
    public void BaseDrawsCenteredFrame()
    {
        var probe = new ProbeDialog { WantW = 30, WantH = 8 };
        probe.Draw(_scr, _theme, _loc);
        Assert.Equal(1, probe.DrawCalls);
        Assert.Equal('┌', CellAt(_scr, 33, 10));
        Assert.Equal('┐', CellAt(_scr, 62, 10));
        Assert.Equal('└', CellAt(_scr, 33, 17));
        Assert.Equal('┘', CellAt(_scr, 62, 17));
        Assert.Equal('│', CellAt(_scr, 33, 12));
        Assert.Equal('│', CellAt(_scr, 62, 12));
        probe.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.False(probe.Closed); // закрытие решает наследник, не база
    }

    [Fact]
    public void StringHelpers()
    {
        Assert.Equal("  ab  ", Dialog.CenterPad("ab", 6));
        Assert.Equal("abcd", Dialog.CenterPad("abcdefg", 4));
        Assert.Equal("01...89", Dialog.MiddleTruncate("0123456789", 7));
        Assert.Equal("abc", Dialog.MiddleTruncate("abc", 10));
    }

    [Fact]
    public void ModalCallbackFlow()
    {
        bool fired = false;
        ModalKeyOutcome seen = default;
        var md = new ModalDialog(ModalState.UnsavedQuit(_loc), (_, o) => { fired = true; seen = o; });
        md.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.False(md.Closed);
        Assert.False(fired);
        md.HandleKey(K('\0', ConsoleKey.Enter));
        Assert.True(md.Closed);
        Assert.True(fired);
        Assert.False(seen.Cancelled);
        Assert.Equal(1, seen.Button);
    }

    [Fact]
    public void ModalEscAndHotkey()
    {
        bool fired = false;
        ModalKeyOutcome seen = default;
        var md2 = new ModalDialog(ModalState.UnsavedQuit(_loc), (_, o) => { fired = true; seen = o; });
        md2.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.True(md2.Closed);
        Assert.True(fired);
        Assert.True(seen.Cancelled);

        fired = false;
        var md3 = new ModalDialog(ModalState.UnsavedQuit(_loc), (_, o) => { fired = true; seen = o; });
        md3.HandleKey(K('y', ConsoleKey.Y));
        Assert.True(fired);
        Assert.Equal(0, seen.Button);
    }

    [Fact]
    public void FileDialogFlow()
    {
        var fd = new FileDialog(new FilePickerState(PickerMode.Save, _dir, "demo.txt"), "Open", "Save");
        Assert.False(fd.Closed);
        Assert.Null(fd.Result);
        fd.HandleKey(K('Z', ConsoleKey.Z));
        fd.HandleKey(K('\0', ConsoleKey.LeftArrow));
        fd.HandleKey(K('\0', ConsoleKey.Escape));
        Assert.True(fd.Closed);
        Assert.Null(fd.Result);

        var fd2 = new FileDialog(new FilePickerState(PickerMode.Open, _dir, ""), "Open", "Save");
        fd2.HandleKey(K('\0', ConsoleKey.DownArrow)); // на a.txt
        fd2.HandleKey(K('\0', ConsoleKey.Enter));
        Assert.True(fd2.Closed);
        Assert.Equal(Path.Combine(_dir, "a.txt"), fd2.Result);
        Assert.Null(fd2.Cursor);
    }

    [Fact]
    public void FileDialogCursorAfterDraw()
    {
        var scr2 = new Screen();
        scr2.Resize(96, 28);
        var fd3 = new FileDialog(new FilePickerState(PickerMode.Save, _dir, "demo.txt"), "Open", "Save");
        fd3.Draw(scr2, _theme, _loc);
        Assert.NotNull(fd3.Cursor);
        fd3.Paste("AB");
        fd3.Draw(scr2, _theme, _loc);
        Assert.NotNull(fd3.Cursor);
    }

    [Fact]
    public void SettingsDialogFlow()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "settings.json"));
        bool applied = false;
        var sd = new SettingsDialog(settings, store, () => { applied = true; });
        Assert.False(sd.Closed);
        sd.HandleKey(K('\0', ConsoleKey.DownArrow));
        sd.HandleKey(K('\0', ConsoleKey.DownArrow));
        sd.HandleKey(K('\0', ConsoleKey.RightArrow)); // снять флажок
        Assert.False(settings.SearchMatchCase);
        Assert.True(applied);
        Assert.True(File.Exists(Path.Combine(_cfgDir, "settings.json")));
        sd.HandleKey(K('\0', ConsoleKey.Escape));
        Assert.True(sd.Closed);
    }

    [Fact]
    public void SettingsDialogSwallowsCtrl()
    {
        var store = new SettingsStore(Path.Combine(_cfgDir, "settings.json"));
        var sd2 = new SettingsDialog(new AppSettings(), store, () => { });
        sd2.HandleKey(K('x', ConsoleKey.X, ctrl: true));
        Assert.False(sd2.Closed);
        var scr3 = new Screen();
        scr3.Resize(96, 28);
        sd2.Draw(scr3, _theme, _loc);
        Assert.Null(sd2.Cursor);
    }

    [Fact]
    public void HelpDialogStructureAndScroll()
    {
        var hd = new HelpDialog(_loc);
        Assert.Equal(20, hd.TotalRows); // 7 заголовков + 13 строк, без футера
        Assert.Equal(0, hd.Scroll);
        hd.HandleKey(K('\0', ConsoleKey.DownArrow));
        Assert.Equal(1, hd.Scroll);
        hd.HandleKey(K('x', ConsoleKey.X, ctrl: true));
        Assert.False(hd.Closed); // Ctrl глотается
        var small = new Screen();
        small.Resize(96, 12);
        hd.HandleKey(K('\0', ConsoleKey.End));
        hd.Draw(small, _theme, _loc);
        Assert.Equal(20 - hd.VisibleRows(12), hd.Scroll); // кламп к низу
        hd.HandleKey(K('\0', ConsoleKey.Home));
        hd.Draw(small, _theme, _loc);
        Assert.Equal(0, hd.Scroll);
        hd.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.True(hd.Closed);
    }

    [Fact]
    public void HelpDialogHasNoHighlight()
    {
        var hd = new HelpDialog(_loc);
        hd.Draw(_scr, _theme, _loc);
        // Ни одной ячейки с фоном выделения: структура — только рамками ── ──.
        var cur = (Array)typeof(Screen).GetField("_cur", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_scr)!;
        for (int y = 0; y < 28; y++)
            for (int x = 0; x < 96; x++)
            {
                object? cell = cur.GetValue(x, y);
                object? bg = cell!.GetType().GetProperty("Bg")!.GetValue(cell)!;
                Assert.False(bg.Equals(_theme.ButtonSelBg), $"highlight at {x},{y}");
            }
    }

    [Fact]
    public void AboutHasInfoOnly()
    {
        var m = ModalState.About(_loc, "0.1.0");
        Assert.Equal(ModalKind.About, m.Kind);
        Assert.False(m.Danger);
        Assert.Equal(5, m.Lines.Count);
        Assert.Contains("0.1.0", m.Lines[0]);
        Assert.Equal(string.Empty, m.Hint);
        Assert.Single(m.Buttons);
    }

    private sealed class ProbeDialog : Dialog
    {
        public int WantW = 10, WantH = 4;
        public int DrawCalls;
        protected override string GetTitle(Loc loc) => "Probe";
        protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
        {
            int bw = Math.Min(WantW, screenW), bh = Math.Min(WantH, screenH);
            return new DialogBox((screenW - bw) / 2, (screenH - bh) / 2, bw, bh);
        }
        protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
        {
            DrawCalls++;
        }
        public override void HandleKey(ConsoleKeyInfo key)
        {
        }
    }
}
