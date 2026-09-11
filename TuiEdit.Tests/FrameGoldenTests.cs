using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Whole-frame goldens via the headless RenderFrame: menu, text, gutter,
// sidebar, status and dialogs together. Regenerate with UPDATE_GOLDENS=1.
[Trait("Category", "Integration")]
public sealed class FrameGoldenTests
{
    [Fact]
    public void BasicTextFrame()
    {
        var ed = NewEditor("fn main() {", "    // hello", "}");
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-basic.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void ModifiedSelectionFrame()
    {
        var ed = NewEditor("alpha", "beta", "gamma");
        HandleKey(ed, K('x', ConsoleKey.X)); // modified marker
        HandleKey(ed, K('\0', ConsoleKey.DownArrow, shift: true)); // select a line
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-selection.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void SidebarTreeFrame()
    {
        // Fixed dir name: the panel header shows the root basename.
        string dir = Path.Combine(Path.GetTempPath(), "tui_frame_golden");
        try { Directory.Delete(dir, true); } catch { }
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            File.WriteAllText(Path.Combine(dir, "README.md"), "x");
            File.WriteAllText(Path.Combine(dir, "src", "main.cs"), "x");
            var ed = NewEditor("hello");
            ed.OpenSidebarRoot(dir);
            var sb = (SidebarState)Field(ed, "_sidebar")!;
            sb.EnterSelected(); // expand src
            ed.NewTab(); // second tab: the tab row must start right of the panel
            ActiveBuf(ed).TrySetEnding("lf"); // fresh tabs get the OS default ending
            Frame(ed, 80, 24);
            Golden.AssertMatch("frame-sidebar.en.txt", ScreenOf(ed));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MenuOpenFrame()
    {
        var ed = NewEditor("one", "two");
        HandleKey(ed, K('\0', ConsoleKey.F10)); // open the menu bar
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-menu.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void TabsFrame()
    {
        var ed = NewEditor("first tab");
        ed.NewTab();
        HandleKey(ed, K('s', ConsoleKey.S)); // type into the second tab
        ed.SwitchTab(0);
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-tabs.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void SplitFrame()
    {
        var ed = NewEditor("left", "right");
        ed.SplitPane();
        ActiveBuf(ed).TrySetEnding("lf"); // fresh panes get the OS default ending
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-split.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void FindHighlightFrame()
    {
        var ed = NewEditor("needle in a haystack", "no match here", "another needle");
        SetField(ed, "_lastSearch", "needle");
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-find.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void ModalOverTextFrame()
    {
        var ed = NewEditor("unsaved work");
        HandleKey(ed, K('x', ConsoleKey.X)); // dirty, so quit asks
        HandleKey(ed, K('\x11', ConsoleKey.Q, ctrl: true)); // TryQuit → unsaved modal
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-unsaved.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void SettingsOverTextFrame()
    {
        var ed = NewEditor("settings below");
        var settings = (AppSettings)Field(ed, "_settings")!;
        var store = new SettingsStore(Path.Combine("x", "settings.json"));
        SetField(ed, "_dialog", new SettingsDialog(settings, store, () => { }));
        Frame(ed, 80, 24);
        Golden.AssertMatch("frame-settings.en.txt", ScreenOf(ed));
    }

    [Fact]
    public void WrapFrame()
    {
        var ed = NewEditor(new string('w', 100));
        var settings = (AppSettings)Field(ed, "_settings")!;
        settings.WordWrap = true;
        Frame(ed, 60, 12);
        Golden.AssertMatch("frame-wrap.en.txt", ScreenOf(ed));
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        buf.TrySetEnding("lf"); // DefaultEnding is OS-dependent; goldens need LF everywhere
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: KeyMap.Current);
    }

    private static void Frame(TuiEditor ed, int w, int h) => ed.RenderFrame(w, h);

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static Screen ScreenOf(TuiEditor ed) =>
        (Screen)typeof(TuiEditor).GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static object? Field(object o, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);

    private static void SetField(object o, string name, object? value) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool ctrl = false) =>
        new(c, k, shift, false, ctrl);

    [Fact]
    public void RenderThrottleWindows()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(TuiEditor.ShouldRender(DateTime.MinValue, t0)); // first frame always due
        Assert.False(TuiEditor.ShouldRender(t0, t0.AddMilliseconds(TuiEditor.RenderThrottleMs - 1)));
        Assert.True(TuiEditor.ShouldRender(t0, t0.AddMilliseconds(TuiEditor.RenderThrottleMs)));
    }
}
