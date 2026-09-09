using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Единый механизм диалогов.</summary>
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

    private static Rgb FgAt(Screen scr, int x, int y)
    {
        var cur = (Array)typeof(Screen).GetField("_cur", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scr)!;
        object? cell = cur.GetValue(x, y);
        return (Rgb)cell!.GetType().GetProperty("Fg")!.GetValue(cell)!;
    }

    private static Rgb BgAt(Screen scr, int x, int y)
    {
        var cur = (Array)typeof(Screen).GetField("_cur", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scr)!;
        object? cell = cur.GetValue(x, y);
        return (Rgb)cell!.GetType().GetProperty("Bg")!.GetValue(cell)!;
    }

    [Fact]
    public void SpanHelpers()
    {
        Assert.Equal("abc", Dialog.StripSpans("`a`b`c`"));
        Assert.Equal(3, Dialog.SpanWidth("`a`b`c`"));
        var scr = new Screen();
        scr.Resize(20, 3);
        var @base = new Rgb(10, 10, 10);
        var accent = new Rgb(20, 20, 20);
        var bg = new Rgb(0, 0, 0);
        Dialog.WriteSpans(scr, 0, 1, "`a`bcd", @base, accent, bg, 10);
        Assert.Equal('a', CellAt(scr, 0, 1));
        Assert.Equal(accent, FgAt(scr, 0, 1));
        Assert.Equal('b', CellAt(scr, 1, 1));
        Assert.Equal(@base, FgAt(scr, 1, 1));
        // Обрезка по видимой ширине (бэктики не считаются).
        Dialog.WriteSpans(scr, 0, 2, "`abcdef", @base, accent, bg, 2);
        Assert.Equal('b', CellAt(scr, 1, 2));
        Assert.Equal('\0', CellAt(scr, 2, 2));
    }

    [Fact]
    public void OptionRowsAlignValues()
    {
        var scr = new Screen();
        scr.Resize(60, 10);
        var box = new DialogBox(5, 1, 40, 6);
        Dialog.DrawOptionRows(scr, _theme, box, ["A", "Longer"], ["x", "yy"], 0);
        int AngleX(int y)
        {
            for (int x = 6; x < 45; x++)
                if (CellAt(scr, x, y) == '<')
                    return x;
            return -1;
        }
        Assert.Equal(AngleX(2), AngleX(3));
        Assert.True(AngleX(2) > 0);
        Assert.Equal('●', CellAt(scr, 7, 2));
        Assert.Equal(' ', CellAt(scr, 7, 3));
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
        var md = new ModalDialog(ModalState.UnsavedQuit(_loc, null), (_, o) => { fired = true; seen = o; });
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
        var md2 = new ModalDialog(ModalState.UnsavedQuit(_loc, null), (_, o) => { fired = true; seen = o; });
        md2.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.True(md2.Closed);
        Assert.True(fired);
        Assert.True(seen.Cancelled);

        fired = false;
        var md3 = new ModalDialog(ModalState.UnsavedQuit(_loc, null), (_, o) => { fired = true; seen = o; });
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
        fd2.HandleKey(K('\0', ConsoleKey.DownArrow));
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
        sd.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.False(settings.ShowLineNumbers);
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
    public void ModalFrameClosesAfterButtons()
    {
        // Регрессия: рамка модалки с кнопками в строку закрывается строго под ними.
        foreach (ModalKind kind in new[] { ModalKind.UnsavedQuit, ModalKind.Overwrite, ModalKind.ReplaceConfirm, ModalKind.Error })
        {
            var scr = new Screen();
            scr.Resize(96, 28);
            ModalState st = kind switch
            {
                ModalKind.Overwrite => ModalState.Overwrite(_loc, "a.txt"),
                ModalKind.ReplaceConfirm => ModalState.ConfirmReplace(_loc, "foo", 60),
                ModalKind.Error => ModalState.Error(_loc, "T", "oops"),
                _ => ModalState.UnsavedQuit(_loc, null),
            };
            new ModalDialog(st, (_, _) => { }).Draw(scr, _theme, _loc);
            var borders = new List<int>();
            for (int y = 0; y < 28; y++)
                for (int x = 0; x < 96; x++)
                    if (CellAt(scr, x, y) == '└')
                    {
                        borders.Add(y);
                        break;
                    }
            Assert.Single(borders);
            string Above(int y)
            {
                var sb = new System.Text.StringBuilder();
                for (int x = 0; x < 96; x++)
                    sb.Append(CellAt(scr, x, y));
                return sb.ToString();
            }
            Assert.DoesNotContain("└", Above(borders[0] - 1));
        }
    }

    [Fact]
    public void ConfirmReplaceShape()
    {
        var m = ModalState.ConfirmReplace(_loc, "foo", 60);
        Assert.Equal(ModalKind.ReplaceConfirm, m.Kind);
        Assert.True(m.Danger);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Equal('Y', m.Buttons[0].Hotkey);
        Assert.Equal('N', m.Buttons[1].Hotkey);
        Assert.Contains("60", m.Lines[0]);
    }

    [Fact]
    public void SettingsDialogRulerCyclesBothWays()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "settings2.json"));
        var sd = new SettingsDialog(settings, store, () => { });
        for (int i = 0; i < 5; i++)
            sd.HandleKey(K('\0', ConsoleKey.DownArrow));
        sd.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal(80, settings.RulerColumn);
        sd.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal(100, settings.RulerColumn);
        sd.HandleKey(K('\0', ConsoleKey.LeftArrow));
        Assert.Equal(80, settings.RulerColumn);
        sd.HandleKey(K('\0', ConsoleKey.LeftArrow));
        Assert.Equal(0, settings.RulerColumn);
        sd.HandleKey(K('\0', ConsoleKey.LeftArrow));
        Assert.Equal(120, settings.RulerColumn);
    }

    [Fact]
    public void HelpDialogStructureAndScroll()
    {
        var hd = new HelpDialog(_loc);
        Assert.Equal(24, hd.TotalRows); // 7 заголовков + 17 строк, без футера
        Assert.Equal(0, hd.Scroll);
        hd.HandleKey(K('\0', ConsoleKey.DownArrow));
        Assert.Equal(1, hd.Scroll);
        hd.HandleKey(K('x', ConsoleKey.X, ctrl: true));
        Assert.False(hd.Closed); // Ctrl глотается
        var small = new Screen();
        small.Resize(96, 12);
        hd.HandleKey(K('\0', ConsoleKey.End));
        hd.Draw(small, _theme, _loc);
        Assert.Equal(24 - HelpDialog.VisibleRows(12), hd.Scroll); // кламп к низу
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
    public void UnsavedShowsFile()
    {
        var m = ModalState.UnsavedQuit(_loc, "a.txt");
        Assert.Contains("a.txt", m.Lines[0]);
        var m2 = ModalState.UnsavedQuit(_loc, null);
        Assert.Equal(_loc["modal.unsaved.desc"], m2.Lines[0]);
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

    [Fact]
    public void BlendMathAndTopY()
    {
        Assert.Equal(new Rgb(50, 50, 50), new Rgb(100, 100, 100).Blend(new Rgb(0, 0, 0), 0.5));
        Assert.Equal(new Rgb(100, 100, 100), new Rgb(100, 100, 100).Blend(new Rgb(0, 0, 0), 0));
        Assert.Equal(new Rgb(0, 0, 0), new Rgb(100, 100, 100).Blend(new Rgb(0, 0, 0), 1));
        Assert.Equal(6, Dialog.TopY(24, 10)); // min(7, 6) — ниже центра
        Assert.Equal(0, Dialog.TopY(24, 30)); // высокое окно — не выше нуля
        Assert.Equal(7, Dialog.TopY(28, 6)); // min(11, 7)
        DialogBox? box = Dialog.MeasureOptions(80, 24, "T", ["A", "B", "C"], ["x", "y", "z"]);
        Assert.NotNull(box);
        Assert.Equal(6, box.Value.Y0); // bh=5: min(9, 6)
    }

    [Fact]
    public void DrawDimsBackdropAndHintsEsc()
    {
        var scr = new Screen();
        scr.Resize(96, 28);
        scr.Text(0, 0, "x", new Rgb(255, 255, 255), new Rgb(100, 100, 100));
        var probe = new ProbeDialog { WantW = 30, WantH = 8 };
        probe.Draw(scr, _theme, _loc);
        Assert.Equal('x', CellAt(scr, 0, 0)); // символ цел, цвета пригашены
        Assert.Equal(new Rgb(255, 255, 255).Blend(new Rgb(0, 0, 0), 0.55), FgAt(scr, 0, 0));
        Assert.Equal(new Rgb(100, 100, 100).Blend(new Rgb(0, 0, 0), 0.55), BgAt(scr, 0, 0));
        // esc справа в строке заголовка (бокс x0=33,w=30 → 59..61,y=10)
        Assert.Equal('e', CellAt(scr, 59, 10));
        Assert.Equal('s', CellAt(scr, 60, 10));
        Assert.Equal('c', CellAt(scr, 61, 10));

        var legacy = new Screen();
        legacy.Resize(96, 28);
        legacy.TrueColor = false;
        legacy.Text(0, 0, "x", new Rgb(255, 255, 255), new Rgb(100, 100, 100));
        probe.Draw(legacy, _theme, _loc);
        Assert.Equal(new Rgb(100, 100, 100), BgAt(legacy, 0, 0)); // legacy — без затемнения
    }

    [Fact]
    public void PaletteCoversMenusCommandsSettings()
    {
        var loc = Loc.Load("en");
        var settings = new AppSettings();
        List<PaletteEntry> all = CommandPaletteDialog.AllEntries(settings, loc);
        Assert.True(all.Count > 40);
        Assert.DoesNotContain(all, e => e is CommandEntry c && c.Command == EditorCommand.None);
        Assert.Contains(all, e => e is SettingEntry s && s.Row == 9);
        Assert.Contains(all, e => e is CommandEntry c && c.Command == EditorCommand.CopyLine);
        Assert.Contains(all, e => e is CommandEntry c && c.Command == EditorCommand.ListTabs);
        // Фильтр: подпись, значение настройки и шорткат команды.
        Assert.Equal([new SettingEntry(9)],
            CommandPaletteDialog.ApplyFilter(all, "mouse", settings, loc));
        Assert.Equal([new CommandEntry(EditorCommand.CopyLine, "Edit: Copy line", "^C"), new SettingEntry(10)],
            CommandPaletteDialog.ApplyFilter(all, "copy", settings, loc));
        List<PaletteEntry> byShortcut =
            CommandPaletteDialog.ApplyFilter(all, "ctrl+p", settings, loc);
        Assert.Contains(byShortcut, e => e is CommandEntry c && c.Command == EditorCommand.ListTabs);
        Assert.Empty(CommandPaletteDialog.ApplyFilter(all, "zzz", settings, loc));
        Assert.Equal(all.Count, CommandPaletteDialog.ApplyFilter(all, "", settings, loc).Count);
    }

    [Fact]
    public void PaletteKeepsRowAndWindow()
    {
        var st = new CommandPaletteState();
        var view = new List<PaletteEntry>
            { new SettingEntry(0), new SettingEntry(1), new SettingEntry(2), new SettingEntry(3) };
        st.ReplaceView(view, fresh: true);
        st.MoveTo(2, 2);
        Assert.Equal(2, st.Selected);
        st.ReplaceView(view, fresh: false); // тот же фильтр — строка жива
        Assert.Equal(2, st.Selected);
        st.MoveTo(3, 2);
        Assert.Equal(3, st.Selected);
        Assert.Equal(2, st.Top); // окно дотянулось
        st.Move(-1, 2);
        Assert.Equal(2, st.Selected);
        st.ReplaceView([], fresh: false); // пусто — безопасно
        st.MoveTo(1, 2);
        Assert.Equal(0, st.Selected);
    }

    [Fact]
    public void PaletteDialogFiltersAndApplies()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "pal.json"));
        bool changed = false;
        EditorCommand? picked = null;
        var dlg = new CommandPaletteDialog(settings, store, () => { changed = true; }, cmd => picked = cmd);
        Assert.Equal(MouseLevel.Off, settings.Mouse);
        dlg.Paste("мышь"); // ru-локаль фикстуры
        var scr = new Screen();
        scr.Resize(80, 24);
        // Клик по первой видимой строке (заголовок + фильтр → +2).
        var box = (DialogBox?)typeof(Dialog)
            .GetMethod("Measure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dlg, [80, 24, _loc]);
        Assert.NotNull(box);
        dlg.HandleClick(box.Value.X0 + 2, box.Value.Y0 + 2, 80, 24, _loc);
        Assert.Equal(MouseLevel.Basic, settings.Mouse);
        Assert.True(changed);
        Assert.Null(picked); // настройка — остаёмся открытыми
        Assert.False(dlg.Closed);
        Assert.True(File.Exists(Path.Combine(_cfgDir, "pal.json")));
    }

    [Fact]
    public void PaletteCommandPickCloses()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "pal4.json"));
        EditorCommand? picked = null;
        var dlg = new CommandPaletteDialog(settings, store, () => { }, cmd => picked = cmd);
        dlg.Paste("ctrl+p"); // шорткат списка вкладок
        var scr = new Screen();
        scr.Resize(80, 24);
        var box = (DialogBox?)typeof(Dialog)
            .GetMethod("Measure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dlg, [80, 24, _loc]);
        Assert.NotNull(box);
        dlg.HandleClick(box.Value.X0 + 2, box.Value.Y0 + 2, 80, 24, _loc);
        Assert.Equal(EditorCommand.ListTabs, picked);
        Assert.True(dlg.Closed);
    }

    [Fact]
    public void PaletteEscClearsFilterThenCloses()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "pal2.json"));
        var dlg = new CommandPaletteDialog(settings, store, () => { }, _ => { });
        dlg.Paste("мышь");
        dlg.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.False(dlg.Closed); // первый Esc — чистит фильтр
        dlg.HandleKey(K('a', ConsoleKey.A));
        Assert.False(dlg.Closed);
        dlg.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.False(dlg.Closed); // снова чистит
        dlg.HandleKey(K('\x1B', ConsoleKey.Escape));
        Assert.True(dlg.Closed); // пустой фильтр — закрыть
    }

    [Fact]
    public void PaletteCursorFollowsFilter()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(_cfgDir, "pal3.json"));
        var dlg = new CommandPaletteDialog(settings, store, () => { }, _ => { });
        var scr = new Screen();
        scr.Resize(80, 24);
        dlg.Paste("мышь"); // 4 символа, одна строка
        var probe = new CommandPaletteDialog(settings, store, () => { }, _ => { });
        probe.Paste("zzz"); // 3 символа, ноль строк — та же высота бокса
        var scr2 = new Screen();
        scr2.Resize(80, 24);
        dlg.Draw(scr, _theme, _loc);
        probe.Draw(scr2, _theme, _loc);
        Assert.NotNull(dlg.Cursor);
        Assert.NotNull(probe.Cursor);
        Assert.Equal(probe.Cursor.Value.x + 1, dlg.Cursor.Value.x); // +разница фильтров
        Assert.Equal(probe.Cursor.Value.y, dlg.Cursor.Value.y);
    }

    [Fact]
    public void PaletteBoundToF5()
    {
        Assert.Equal(EditorCommand.CommandPalette,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false)));
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
