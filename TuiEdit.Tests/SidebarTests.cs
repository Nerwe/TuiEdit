using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Панель файлов: модель, Ctrl+B, фокус и открытие (ширина фиксирована).</summary>
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
        // Строка вкладок над панелью не должна светить старым текстом.
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
            // Скрытость важнее типа.
            Assert.Equal(theme.PickerHiddenFg, TuiEditor.EntryFg(theme, new SidebarEntry(".h", true, IsHidden: true)));
            // Цвета типов различимы между собой.
            Assert.NotEqual(theme.PickerDirFg, theme.PickerExeFg);
            Assert.NotEqual(theme.PickerDirFg, theme.EditorFg);
        }
    }

    [Fact]
    public void ModelListsDirsFirst()
    {
        var sb = new SidebarState(_root);
        Assert.Equal(Path.GetFullPath(_root), sb.CurrentDir);
        Assert.True(sb.Entries.Count >= 4); // .., sub, a.txt, b.txt
        Assert.Equal("..", sb.Entries[0].Name);
        Assert.True(sb.Entries[0].IsDir);
        Assert.Equal("sub", sb.Entries[1].Name);
        Assert.DoesNotContain(sb.Entries, e => e.Name == "a.txt" && e.IsDir);
    }

    [Fact]
    public void ModelEnterAndScroll()
    {
        var sb = new SidebarState(_root);
        // Вниз до файла: Enter по файлу — false (открывает редактор).
        while (!sb.Entries[sb.Selected].Name.Equals("a.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        Assert.False(sb.EnterSelected());
        Assert.EndsWith("a.txt", sb.SelectedPath);

        // Enter по папке — зайти.
        var sb2 = new SidebarState(_root);
        while (!sb2.Entries[sb2.Selected].Name.Equals("sub", StringComparison.Ordinal))
            sb2.MoveHighlight(1, 10);
        Assert.True(sb2.EnterSelected());
        Assert.EndsWith("sub", sb2.CurrentDir);
        Assert.Equal(0, sb2.Selected);

        // Скролл окном 2.
        var sb3 = new SidebarState(_root);
        sb3.MoveHighlight(3, 2);
        Assert.Equal(3, sb3.Selected);
        Assert.Equal(2, sb3.Top);
        sb3.MoveHighlight(-3, 2);
        Assert.Equal(0, sb3.Selected);
        Assert.Equal(0, sb3.Top);
    }

    [Fact]
    public void ModelBadDirSilent()
    {
        var sb = new SidebarState(Path.Combine(_root, "ghost", "\0??"));
        Assert.Empty(sb.Entries);
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
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // открыть + фокус
        Assert.NotNull(Field(ed, "_sidebar"));
        Assert.True((bool)Field(ed, "_sidebarFocus")!);
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // в фокусе — закрыть
        Assert.Null(Field(ed, "_sidebar"));
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        HandleKey(ed, K('\x1B', ConsoleKey.Escape)); // Esc — фокус в текст, панель жива
        Assert.NotNull(Field(ed, "_sidebar"));
        Assert.False((bool)Field(ed, "_sidebarFocus")!);
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true)); // без фокуса — вернуть фокус
        Assert.True((bool)Field(ed, "_sidebarFocus")!);
    }

    [Fact]
    public void EnterOpensFile()
    {
        var ed = NewEditor();
        // Корень панели — cwd; переходим в тестовую папку напрямую через модель.
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        var sb = (SidebarState)Field(ed, "_sidebar")!;
        sb.NavigateTo(_root);
        while (!sb.Entries[sb.Selected].Name.Equals("b.txt", StringComparison.Ordinal))
            sb.MoveHighlight(1, 10);
        HandleKey(ed, K('\0', ConsoleKey.Enter));
        // _buf — свойство активной вкладки (не поле).
        var buf = (TextBuffer)typeof(TuiEditor)
            .GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        Assert.Equal(Path.Combine(_root, "b.txt"), buf.FilePath);
        Assert.False((bool)Field(ed, "_sidebarFocus")!);
        Assert.NotNull(Field(ed, "_sidebar")); // панель осталась открытой
    }

    [Fact]
    public void TypingSwallowedInFocus()
    {
        var ed = NewEditor();
        HandleKey(ed, K('\x02', ConsoleKey.B, ctrl: true));
        var buf = (TextBuffer)typeof(TuiEditor)
            .GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        int before = buf.Count;
        HandleKey(ed, K('x', ConsoleKey.X));
        Assert.Equal(before, buf.Count); // печать в фокусе панели глотается
    }
}
