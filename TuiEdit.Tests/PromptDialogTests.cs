using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Single-line prompt: typing, arrows, Enter accepts, Esc cancels.
public sealed class PromptDialogTests
{
    private readonly Theme _theme = Themes.Get("dark");
    private readonly Loc _loc = Loc.Load("en");

    private static Screen NewScreen()
    {
        var scr = new Screen();
        scr.Resize(80, 24);
        return scr;
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k) =>
        new(c, k, false, false, false);

    [Fact]
    public void TypeAndAccept()
    {
        string? done = null;
        var dlg = new PromptDialog("Title", "Hint", string.Empty, t => done = t);
        dlg.HandleKey(K('a', ConsoleKey.A));
        dlg.HandleKey(K('b', ConsoleKey.B));
        Assert.Equal("ab", dlg.Text);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
        Assert.True(dlg.Closed);
        Assert.Equal("ab", done);
    }

    [Fact]
    public void EscapeCancels()
    {
        string? done = string.Empty;
        bool called = false;
        var dlg = new PromptDialog("Title", "Hint", "pre", t => { called = true; done = t; });
        dlg.HandleKey(K('x', ConsoleKey.X));
        dlg.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.True(dlg.Closed);
        Assert.True(called);
        Assert.Null(done);
    }

    [Fact]
    public void ArrowsMoveCursor()
    {
        string? done = null;
        var dlg = new PromptDialog("Title", "Hint", "ab", t => done = t);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        dlg.HandleKey(K('X', ConsoleKey.X));
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
        Assert.Equal("aXb", done);
    }

    [Fact]
    public void FrameGolden()
    {
        var scr = NewScreen();
        new PromptDialog("Rename", "Enter accepts, Esc cancels", "old.txt", _ => { })
            .Draw(scr, _theme, _loc);
        Golden.AssertMatch("prompt.en.txt", scr);
    }
}
