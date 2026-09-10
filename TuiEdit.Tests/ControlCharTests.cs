using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>
/// Control characters (ESC and friends) must never reach the console:
/// a raw ESC inside a frame makes the terminal swallow all following output,
/// which looks like a total freeze (dead screen, blinking cursor, no errors).
/// </summary>
public sealed class ControlCharTests
{
    [Fact]
    public void SetSanitizesControlChars()
    {
        var theme = Themes.Get("dark");
        var scr = new Screen();
        scr.Resize(8, 2);
        scr.Set(0, 0, '\x1b', theme.EditorFg, theme.EditorBg);
        scr.Set(1, 0, '\x07', theme.EditorFg, theme.EditorBg);
        scr.Set(2, 0, '\0', theme.EditorFg, theme.EditorBg);
        scr.Set(3, 0, '\t', theme.EditorFg, theme.EditorBg);
        scr.Set(4, 0, 'a', theme.EditorFg, theme.EditorBg);
        Assert.Equal('�', scr.At(0, 0).Ch);
        Assert.Equal('�', scr.At(1, 0).Ch);
        Assert.Equal('\0', scr.At(2, 0).Ch); // unwritten-cell sentinel passes through
        Assert.Equal(' ', scr.At(3, 0).Ch);
        Assert.Equal('a', scr.At(4, 0).Ch);
    }

    [Fact]
    public void FrameWithEscapeSequenceEmitsNoControlCells()
    {
        // An ANSI-colored line (e.g. pasted from terminal output): after any edit
        // scrolls it into view, the frame must stay printable.
        var ed = NewEditor("plain", "a\x1b[31mred\x1b[0m");
        ed.RenderFrame(80, 24);
        var scr = ScreenOf(ed);
        foreach (string row in scr.Snapshot())
            foreach (char c in row)
                Assert.False(char.IsControl(c), $"control char U+{(int)c:X4} reaches the screen");
        Assert.Contains("�", scr.Snapshot()[2]);
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        buf.TrySetEnding("lf"); // DefaultEnding is OS-dependent; keep it deterministic
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: KeyMap.Current);
    }

    private static Screen ScreenOf(TuiEditor ed) =>
        (Screen)typeof(TuiEditor).GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
}
