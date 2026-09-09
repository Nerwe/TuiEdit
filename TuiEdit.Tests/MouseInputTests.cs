using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>SGR mouse: burst parser and click-to-document mapping.</summary>
public sealed class MouseInputTests
{
    [Theory]
    [InlineData("[<0;11;6M", 10, 5, 0)] // 1-based -> 0-based, LeftPress
    [InlineData("[<0;1;1M", 0, 0, 0)]
    [InlineData("[<32;5;5M", 4, 4, 3)] // motion (?1002) — hover
    [InlineData("[<64;5;5M", 4, 4, 1)] // WheelUp
    [InlineData("[<65;5;5M", 4, 4, 2)] // WheelDown
    [InlineData("[<72;5;5M", 4, 4, 1)] // wheel + shift modifier
    [InlineData("[<0;11;6m", 10, 5, 3)] // release: hover position only
    [InlineData("[<1;11;6M", 10, 5, 4)] // middle button — an event, not null
    [InlineData("[<2;11;6M", 10, 5, 5)] // right button — an event, not null
    public void ParseValid(string burst, int x, int y, int action)
    {
        MouseInput? m = MouseInput.TryParse(burst);
        Assert.NotNull(m);
        Assert.Equal(x, m.X);
        Assert.Equal(y, m.Y);
        Assert.Equal((MouseAction)action, m.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[200~")]

    [InlineData("[<0;5M")] // too few parts
    [InlineData("[<0;5;5X")] // not M/m
    [InlineData("[<a;5;5M")] // not a number
    [InlineData("[A")] // arrow — not mouse
    public void ParseInvalid(string burst)
    {
        Assert.Null(MouseInput.TryParse(burst));
    }

    [Fact]
    public void ParseKeepsButtonAndModifiers()
    {
        MouseInput? left = MouseInput.TryParse("[<0;5;5M");
        Assert.NotNull(left);
        Assert.Equal(MouseButton.Left, left.Button);
        Assert.Equal(MouseModifiers.None, left.Modifiers);
        Assert.Equal(1, left.Count);

        MouseInput? mid = MouseInput.TryParse("[<9;5;5M"); // middle + alt (1|8)
        Assert.NotNull(mid);
        Assert.Equal(MouseAction.MiddlePress, mid.Action);
        Assert.Equal(MouseButton.Middle, mid.Button);
        Assert.Equal(MouseModifiers.Alt, mid.Modifiers);

        MouseInput? right = MouseInput.TryParse("[<22;5;5M"); // right + shift + ctrl (2|4|16)
        Assert.NotNull(right);
        Assert.Equal(MouseAction.RightPress, right.Action);
        Assert.Equal(MouseButton.Right, right.Button);
        Assert.Equal(MouseModifiers.Shift | MouseModifiers.Ctrl, right.Modifiers);

        MouseInput? drag = MouseInput.TryParse("[<32;5;5M"); // motion with held left
        Assert.NotNull(drag);
        Assert.Equal(MouseAction.Move, drag.Action);
        Assert.Equal(MouseButton.Left, drag.Button);

        MouseInput? release = MouseInput.TryParse("[<0;5;5m");
        Assert.NotNull(release);
        Assert.Equal(MouseAction.Move, release.Action);
        Assert.Equal(MouseButton.None, release.Button);
    }

    private static Terminal.MouseEventRecord Rec(uint buttons, uint flags, short x, short y) =>
        new() { ButtonState = buttons, EventFlags = flags, MousePosition = new Terminal.Coord { X = x, Y = y } };

    [Theory]
    [InlineData(0x0001u, 0u, 10, 5, 0)] // left click
    [InlineData(0x0001u, 0x0002u, 10, 5, 0)] // double-click (second press) — also a click
    [InlineData(0x0000u, 0u, 10, 5, 3)] // release: hover position only
    [InlineData(0x0000u, 0x0002u, 10, 5, -1)] // double-click flag without a button — ignore
    [InlineData(0x0002u, 0u, 10, 5, -1)] // right — ignore
    [InlineData(0x0001u, 0x0001u, 10, 5, 3)] // motion — hover (one per Read)
    [InlineData(0x00780000u, 0x0004u, 3, 7, 1)] // wheel up (delta +120)
    [InlineData(0xFF880000u, 0x0004u, 3, 7, 2)] // wheel down (delta -120)
    [InlineData(0x00000000u, 0x0004u, 3, 7, -1)] // wheel with zero delta — ignore
    public void TranslateConsoleRecord(uint buttons, uint flags, short x, short y, int action)
    {
        Terminal.MouseEventRecord r = Rec(buttons, flags, x, y);
        if (action < 0)
        {
            Assert.Null(Terminal.TranslateMouse(r));
            return;
        }
        MouseInput? m = Terminal.TranslateMouse(r);
        Assert.NotNull(m);
        Assert.Equal((MouseAction)action, m.Action);
        Assert.Equal(x, m.X);
        Assert.Equal(y, m.Y);
    }

    [Fact]
    public void TranslateKeepsButtonAndModifiers()
    {
        Terminal.MouseEventRecord r = Rec(0x0001u, 0u, 4, 6);
        r.ControlKeyState = 0x0010u; // Shift
        MouseInput? m = Terminal.TranslateMouse(r);
        Assert.NotNull(m);
        Assert.Equal(MouseAction.LeftPress, m.Action);
        Assert.Equal(MouseButton.Left, m.Button);
        Assert.Equal(MouseModifiers.Shift, m.Modifiers);
        Assert.Equal(1, m.Count);

        Terminal.MouseEventRecord w = Rec(0x00780000u, 0x0004u, 3, 7);
        w.ControlKeyState = 0x0004u | 0x0002u; // Ctrl + Alt
        MouseInput? wm = Terminal.TranslateMouse(w);
        Assert.NotNull(wm);
        Assert.Equal(MouseAction.WheelUp, wm.Action);
        Assert.Equal(MouseButton.None, wm.Button);
        Assert.Equal(MouseModifiers.Ctrl | MouseModifiers.Alt, wm.Modifiers);
    }

    [Fact]
    public void TranslateClampsNegativeCoords()
    {
        MouseInput? m = Terminal.TranslateMouse(Rec(0x0001u, 0u, -5, -2));
        Assert.NotNull(m);
        Assert.Equal(0, m.X);
        Assert.Equal(0, m.Y);
    }

    [Fact]
    public void ColumnAtPlainAndTabs()
    {
        Assert.Equal(0, TuiEditor.ColumnAt("hello", 0));
        Assert.Equal(3, TuiEditor.ColumnAt("hello", 3));
        Assert.Equal(5, TuiEditor.ColumnAt("hello", 99)); // past the end — do not run away (clamped above)
        Assert.Equal(1, TuiEditor.ColumnAt("\thello", 4)); // tab of width 4
        Assert.Equal(0, TuiEditor.ColumnAt("\thello", 3)); // middle of a tab — left edge
    }

    [Fact]
    public void LocateClickPlain()
    {
        var lines = new List<string> { "aaa", "bbb", "ccc" };
        var folds = new SortedSet<int>();
        Assert.Equal((1, 2), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 1, 2));
        Assert.Equal((0, 0), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 0, 0));
        // Below the text — end of document.
        Assert.Equal((2, 3), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 9, 0));
    }

    [Fact]
    public void LocateClickWrapAndScroll()
    {
        var lines = new List<string> { "aaa", "bbb" };
        var folds = new SortedSet<int>();
        // Scroll top=1: vis 0 — second row.
        Assert.Equal((1, 1), TuiEditor.LocateClick(lines, folds, 1, 0, false, 80, 0, 0, 1));
        // Horizontal scroll left=2: the column counts from it.
        Assert.Equal((0, 3), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 2, 0, 1));
    }

    [Fact]
    public void LocateClickSkipsFolds()
    {
        var lines = new List<string> { "if x:", "    a", "    b", "done" };
        var folds = new SortedSet<int> { 0 }; // rows 1-2 hidden (by indent)
        Assert.Equal((0, 1), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 0, 1));
        Assert.Equal((3, 0), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 1, 0));
    }
}
