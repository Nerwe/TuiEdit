using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class CommentToggleTests
{
    private static TextBuffer Buf(params string[] lines)
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string>(lines));
        return b;
    }

    [Fact]
    public void CommentAddsMarkerAfterIndent()
    {
        var b = Buf("int x;", "    int y;", "");
        int[] d = b.ToggleLineComment(0, 2, "//");
        Assert.Equal(new[] { "// int x;", "    // int y;", "// " }, b.Lines);
        Assert.Equal(new[] { 3, 3, 3 }, d);
    }

    [Fact]
    public void UncommentRemovesMarkerAndSpace()
    {
        var b = Buf("// int x;", "    //int y;");
        int[] d = b.ToggleLineComment(0, 1, "//");
        Assert.Equal(new[] { "int x;", "    int y;" }, b.Lines);
        Assert.Equal(new[] { -3, -2 }, d);
    }

    [Fact]
    public void MixedRangeCommentsAll()
    {
        var b = Buf("// a;", "b;");
        b.ToggleLineComment(0, 1, "#");
        Assert.Equal(new[] { "# // a;", "# b;" }, b.Lines);
    }

    [Fact]
    public void ToggleRoundTripAndSingleUndo()
    {
        var b = Buf("a;", "b;");
        b.ToggleLineComment(0, 1, "#");
        b.ToggleLineComment(0, 1, "#");
        Assert.Equal(new[] { "a;", "b;" }, b.Lines);
        b.Undo();
        Assert.Equal(new[] { "# a;", "# b;" }, b.Lines);
    }

    [Fact]
    public void HashCommentForPython()
    {
        var b = Buf("x = 1");
        b.ToggleLineComment(0, 0, "#");
        Assert.Equal(new[] { "# x = 1" }, b.Lines);
    }

    [Fact]
    public void CtrlSlashMapsToToggleComment()
    {
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, true)));
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('\x1F', ConsoleKey.Oem2, false, false, true)));
    }
}
