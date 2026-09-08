using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class LineFieldTests
{
    [Fact]
    public void InsertTypeAndMove()
    {
        var f = new LineField();
        f.Set("hello");
        f.End(select: false);
        Assert.Equal(5, f.Pos);
        f.Move(-2, select: false);
        Assert.Equal(3, f.Pos);
        f.Insert("X");
        Assert.Equal("helXlo", f.Text);
        Assert.Equal(4, f.Pos);
    }

    [Fact]
    public void SelectionReplaceAndCollapse()
    {
        var f = new LineField();
        f.Set("hello");
        f.Home(select: false);
        f.Move(3, select: true);
        Assert.True(f.HasSelection);
        f.GetSelection(out int a, out int b);
        Assert.Equal((0, 3), (a, b));
        f.Insert("HE");
        Assert.Equal("HElo", f.Text);
        Assert.False(f.HasSelection);
        f.Move(-10, select: false);
        Assert.False(f.HasSelection);
        Assert.Equal(0, f.Pos);
    }

    [Fact]
    public void WordMotionAndDelete()
    {
        var f = new LineField();
        f.Set("foo bar");
        f.End(select: false);
        f.MoveWord(-1, select: false);
        Assert.Equal(4, f.Pos);
        f.DeleteWord(-1);
        Assert.Equal("bar", f.Text);
        Assert.Equal(0, f.Pos);
        f.Set("foo bar");
        f.Home(select: false);
        f.MoveWord(1, select: true);
        f.GetSelection(out int a, out int b);
        Assert.Equal((0, 3), (a, b));
    }

    [Fact]
    public void BackspaceDeleteCharEdges()
    {
        var f = new LineField();
        f.Set("ab");
        f.Home(select: false);
        f.Backspace();
        Assert.Equal("ab", f.Text);
        f.DeleteChar();
        Assert.Equal("b", f.Text);
        f.End(select: false);
        f.DeleteChar();
        Assert.Equal("b", f.Text);
        f.Set(null!);
        Assert.Equal("", f.Text);
    }
}
