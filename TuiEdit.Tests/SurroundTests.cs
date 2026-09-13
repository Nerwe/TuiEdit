using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Surround: wrap/change/unwrap as single undo entries.</summary>
public sealed class SurroundTests
{
    [Fact]
    public void WrapSingleLineIsSingleUndo()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["hello"]);
        buf.WrapRange(0, 0, 0, 5, '(', ')');
        Assert.Equal("(hello)", buf.GetLine(0));
        buf.Undo();
        Assert.Equal("hello", buf.GetLine(0));
    }

    [Fact]
    public void WrapMultilineInsertsEdges()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["ab", "cd"]);
        buf.WrapRange(0, 1, 1, 1, '[', ']');
        Assert.Equal("a[b", buf.GetLine(0));
        Assert.Equal("c]d", buf.GetLine(1));
        buf.Undo();
        Assert.Equal(["ab", "cd"], buf.Lines);
    }

    [Fact]
    public void UnwrapRemovesPair()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["(hi)"]);
        Assert.True(buf.UnwrapRange(0, 1, 0, 3, '(', ')'));
        Assert.Equal("hi", buf.GetLine(0));
        buf.Undo();
        Assert.Equal("(hi)", buf.GetLine(0));
    }

    [Fact]
    public void UnwrapMismatchFailsWithoutUndo()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["hi"]);
        Assert.False(buf.UnwrapRange(0, 0, 0, 2, '(', ')'));
        Assert.False(buf.CanUndo);
    }

    [Fact]
    public void ChangeSurroundSwapsPair()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["(hi)"]);
        Assert.True(buf.ChangeSurround(0, 1, 0, 3, '(', ')', '[', ']'));
        Assert.Equal("[hi]", buf.GetLine(0));
        buf.Undo();
        Assert.Equal("(hi)", buf.GetLine(0));
    }

    [Fact]
    public void CloserForSurroundFallsBackToSelf()
    {
        Assert.Equal(')', TextBuffer.CloserForSurround('('));
        Assert.Equal('"', TextBuffer.CloserForSurround('"'));
        Assert.Equal('*', TextBuffer.CloserForSurround('*'));
    }

    [Fact]
    public void SurroundKeysAreBound()
    {
        Assert.Equal(EditorCommand.SurroundAdd,
            KeyMap.Map(new ConsoleKeyInfo('\f', ConsoleKey.L, false, false, true)));
        Assert.Equal(EditorCommand.SurroundChange,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.L, false, true, false)));
        Assert.Equal(EditorCommand.SurroundDelete,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.J, false, true, false)));
    }
}
