using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class BracketMatchTests
{
    private static TextBuffer Buf(params string[] lines)
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string>(lines));
        return b;
    }

    private static (int Row, int Col)? Match(TextBuffer b, int row, int col)
    {
        GrammarRegistry.EnsureLoaded();
        return BracketMatcher.FindMatch(b, new SyntaxHighlighter(), GrammarRegistry.ForExtension(".cs"), row, col);
    }

    [Fact]
    public void ForwardMatch()
    {
        var b = Buf("f(a, (b));");
        Assert.Equal((0, 8), Match(b, 0, 1));
        Assert.Equal((0, 1), Match(b, 0, 8));
    }

    [Fact]
    public void MultilineMatch()
    {
        var b = Buf("if (x", "    && y) {", "}");
        Assert.Equal((1, 8), Match(b, 0, 3));
        Assert.Equal((0, 3), Match(b, 1, 8));
        Assert.Equal((1, 10), Match(b, 2, 0));
    }

    [Fact]
    public void SkipsStringAndComment()
    {
        var b = Buf("s = \"(\"; // )", "x = (1);");
        Assert.Equal((1, 6), Match(b, 1, 4));
        Assert.Null(Match(b, 0, 5));
        Assert.Null(Match(b, 0, 12));
    }

    [Fact]
    public void NoBracketOrUnmatchedGivesNull()
    {
        var b = Buf("abc", "(unclosed");
        Assert.Null(Match(b, 0, 1));
        Assert.Null(Match(b, 1, 0));
    }

    [Fact]
    public void CursorAfterBracketUsesPrevious()
    {
        var b = Buf("(x)");
        Assert.Equal((0, 2), Match(b, 0, 0));
        Assert.Equal((0, 0), Match(b, 0, 2));
        Assert.Equal((0, 0), Match(b, 0, 3));
    }

    [Fact]
    public void AltRightBracketMapsToJump()
    {
        Assert.Equal(EditorCommand.GoBracketMatch,
            KeyMap.Map(new ConsoleKeyInfo(']', ConsoleKey.Oem6, false, true, false)));
    }
}
