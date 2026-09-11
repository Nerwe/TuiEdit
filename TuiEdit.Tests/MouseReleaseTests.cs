using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>
/// Clicks fire on release, not on press (Jumbee-style): press arms, release on
/// the same target activates, releasing elsewhere cancels. Text/drag/tab behavior stays on press.
/// </summary>
public sealed class MouseReleaseTests
{
    private const int W = 80;
    private const int H = 24;

    private static Loc En() => Loc.Load("en");

    private static void WithMouse(Action body)
    {
        InputReader.MouseLevel = MouseLevel.Basic;
        try
        {
            body();
        }
        finally
        {
            InputReader.MouseLevel = MouseLevel.Off;
        }
    }

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "line" });
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: KeyMap.Current);
    }

    private static void SetDialog(TuiEditor ed, ModalDialog dlg) =>
        typeof(TuiEditor).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(ed, dlg);

    private static ModalDialog? Dialog(TuiEditor ed) =>
        typeof(TuiEditor).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed) as ModalDialog;

    private static object? Menu(TuiEditor ed) =>
        typeof(TuiEditor).GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed);

    private static MenuState MenuStateOf(TuiEditor ed) =>
        (MenuState)typeof(TuiEditor).GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static Screen ScreenOf(TuiEditor ed) =>
        (Screen)typeof(TuiEditor).GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static Theme ThemeOf(TuiEditor ed) =>
        (Theme)typeof(TuiEditor).GetField("_theme", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static void SetMouse(TuiEditor ed, bool active, int x, int y)
    {
        var t = typeof(TuiEditor);
        t.GetField("_mouseActive", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ed, active);
        t.GetField("_mouseX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ed, x);
        t.GetField("_mouseY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ed, y);
    }

    private static (int row, int col) Cursor(TuiEditor ed)
    {
        var t = typeof(TuiEditor);
        return (
            (int)t.GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!,
            (int)t.GetField("_col", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!);
    }

    private static void OpenMenu(TuiEditor ed, int index) =>
        typeof(TuiEditor).GetMethod("OpenMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [index]);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static MouseInput Press(int x, int y) => new(x, y, MouseAction.LeftPress, MouseButton.Left);

    private static MouseInput Release(int x, int y) => new(x, y, MouseAction.Move);

    /// <summary>Finds one cell per button of the overwrite dialog (HitButton is pure).</summary>
    private static Dictionary<int, (int x, int y)> ButtonCells()
    {
        var loc = En();
        var cells = new Dictionary<int, (int x, int y)>();
        var probe = new ModalDialog(ModalState.Overwrite(loc, "a.txt"), (_, _) => { });
        for (int y = 0; y < H && cells.Count < 2; y++)
            for (int x = 0; x < W && cells.Count < 2; x++)
            {
                int? hit = probe.HitButton(x, y, W, H, loc);
                if (hit is not null && !cells.ContainsKey(hit.Value))
                    cells[hit.Value] = (x, y);
            }
        return cells;
    }

    private static (TuiEditor ed, List<int> fired) DialogEditor()
    {
        var ed = NewEditor();
        var fired = new List<int>();
        SetDialog(ed, new ModalDialog(ModalState.Overwrite(En(), "a.txt"),
            (_, o) => { if (!o.Cancelled) fired.Add(o.Button); }));
        return (ed, fired);
    }

    [Fact]
    public void DialogPressReleaseSameCellFiresOnce()
    {
        WithMouse(() =>
        {
            var cells = ButtonCells();
            Assert.Equal(2, cells.Count);
            foreach ((int want, (int x, int y)) in cells)
            {
                var (ed, fired) = DialogEditor();
                ed.HandleMouseAt(Press(x, y), W, H);
                Assert.Empty(fired); // press alone arms, never fires
                ed.HandleMouseAt(Release(x, y), W, H);
                Assert.Equal(new[] { want }, fired);
            }
        });
    }

    [Fact]
    public void DialogDragOffCancels()
    {
        WithMouse(() =>
        {
            var cells = ButtonCells();
            var (ax, ay) = cells[0];
            var (bx, by) = cells[1];
            var (ed, fired) = DialogEditor();
            ed.HandleMouseAt(Press(ax, ay), W, H);
            ed.HandleMouseAt(Release(bx, by), W, H); // released on another button
            Assert.Empty(fired);
            Assert.NotNull(Dialog(ed)); // disarmed, dialog stays open
        });
    }

    [Fact]
    public void DialogWheelDisarms()
    {
        WithMouse(() =>
        {
            var cells = ButtonCells();
            var (x, y) = cells[0];
            var (ed, fired) = DialogEditor();
            ed.HandleMouseAt(Press(x, y), W, H);
            ed.HandleMouseAt(new MouseInput(x, y, MouseAction.WheelUp), W, H);
            ed.HandleMouseAt(Release(x, y), W, H);
            Assert.Empty(fired);
        });
    }

    [Fact]
    public void DialogStaleArmDropped()
    {
        WithMouse(() =>
        {
            var cells = ButtonCells();
            var (x, y) = cells[0];
            var (ed, fired) = DialogEditor();
            ed.HandleMouseAt(Press(x, y), W, H);
            HandleKey(ed, new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)); // closes dialog
            Assert.Null(Dialog(ed));
            ed.HandleMouseAt(Release(x, y), W, H);
            Assert.Empty(fired);
        });
    }

    /// <summary>Finds a dropdown cell whose press+release triggers Undo (dirty buffer reverts).</summary>
    private static (int x, int y) FindUndoCell()
    {
        var loc = En();
        var menus = TuiEditor.BuildMenus(loc);
        var edit = menus[1];
        int menuX = menus[0].Label.Length + 2;
        for (int y = 1; y < 14; y++)
            for (int x = menuX; x < menuX + 24; x++)
                if (MenuHit.DropdownHit(edit, menuX, x, y, W, H) == 0)
                    return (x, y);
        throw new Xunit.Sdk.XunitException("Undo cell not found");
    }

    private static TuiEditor DirtyEditor()
    {
        var ed = NewEditor();
        var buf = (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
        buf.InsertChar(0, 0, 'X');
        return ed;
    }

    private static string FirstLine(TuiEditor ed) =>
        ((TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!).GetLine(0);

    [Fact]
    public void MenuReleaseActivatesItem()
    {
        WithMouse(() =>
        {
            var (x, y) = FindUndoCell();
            var ed = DirtyEditor();
            Assert.StartsWith("X", FirstLine(ed));
            OpenMenu(ed, 1);
            ed.HandleMouseAt(Press(x, y), W, H);
            Assert.StartsWith("X", FirstLine(ed)); // press alone arms, never fires
            ed.HandleMouseAt(Release(x, y), W, H);
            Assert.Equal("line", FirstLine(ed)); // released: Undo ran
            Assert.Null(Menu(ed));
        });
    }

    [Fact]
    public void MenuReleaseMissClosesWithoutEffect()
    {
        WithMouse(() =>
        {
            var (x, y) = FindUndoCell();
            var ed = DirtyEditor();
            OpenMenu(ed, 1);
            ed.HandleMouseAt(Press(x, y), W, H);
            ed.HandleMouseAt(Release(0, H - 1), W, H); // status bar: off-menu
            Assert.Null(Menu(ed));
            Assert.StartsWith("X", FirstLine(ed)); // nothing ran, cursor untouched by release
        });
    }

    /// <summary>Finds a cell on item 0 and a cell on a separator of the File menu.</summary>
    private static ((int x, int y) item, (int x, int y) sep) FileMenuCells()
    {
        var menus = TuiEditor.BuildMenus(En());
        var file = menus[0];
        var state = new MenuState(menus);
        state.Open(0);
        int menuX = MenuHit.MenuX(state);
        (int x, int y)? item = null;
        (int x, int y)? sep = null;
        for (int y = 1; y < 14 && (item is null || sep is null); y++)
            for (int x = menuX; x < menuX + 24 && (item is null || sep is null); x++)
            {
                if (item is null && MenuHit.DropdownHit(file, menuX, x, y, W, H) == 0)
                    item = (menuX + 2, y); // label area, not the border cell
                if (sep is null && MenuHit.IsSeparatorHit(file, menuX, x, y, W, H))
                    sep = (menuX + 2, y);
            }
        Assert.True(item is not null && sep is not null, "item and separator cells not found");
        return (item!.Value, sep!.Value);
    }

    [Fact]
    public void MenuSeparatorHoverLightsNothing()
    {
        WithMouse(() =>
        {
            var (item, sep) = FileMenuCells();
            var theme = ThemeOf(NewEditor());
            var ed = NewEditor();
            OpenMenu(ed, 0);
            // Hovering the item lights it.
            SetMouse(ed, true, item.x, item.y);
            ed.RenderFrame(W, H);
            Assert.Equal(theme.DropSelBg, ScreenOf(ed).At(item.x, item.y).Bg);
            // Hovering the separator lights nothing (used to jump to item 0).
            SetMouse(ed, true, sep.x, sep.y);
            ed.RenderFrame(W, H);
            Assert.NotEqual(theme.DropSelBg, ScreenOf(ed).At(item.x, item.y).Bg);
            Assert.NotEqual(theme.DropSelBg, ScreenOf(ed).At(sep.x, sep.y).Bg);
        });
    }

    [Fact]
    public void MenuPressSeparatorKeepsMenuOpen()
    {
        WithMouse(() =>
        {
            var (_, sep) = FileMenuCells();
            var ed = NewEditor();
            OpenMenu(ed, 0);
            ed.HandleMouseAt(Press(sep.x, sep.y), W, H);
            Assert.NotNull(Menu(ed)); // dead zone: no close, no fall-through to text
            Assert.Equal((0, 0), Cursor(ed));
        });
    }

    [Fact]
    public void MenuReleaseSeparatorKeepsMenuOpen()
    {
        WithMouse(() =>
        {
            var (item, sep) = FileMenuCells();
            var ed = NewEditor();
            OpenMenu(ed, 0);
            ed.HandleMouseAt(Press(item.x, item.y), W, H);
            ed.HandleMouseAt(Release(sep.x, sep.y), W, H); // armed, released on separator
            Assert.NotNull(Menu(ed));
            Assert.Equal(1, ed.TabCount); // NewTab did not fire
        });
    }

    [Fact]
    public void MenuBarStillOpensOnPress()
    {
        WithMouse(() =>
        {
            var ed = NewEditor();
            Assert.Null(Menu(ed));
            ed.HandleMouseAt(Press(2, 0), W, H); // over the File title
            Assert.NotNull(Menu(ed));
        });
    }
}
