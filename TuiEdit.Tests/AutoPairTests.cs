using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Автопары скобок и кавычек.</summary>
public sealed class AutoPairTests
{
    [Theory]
    [InlineData('(', ')')]
    [InlineData('[', ']')]
    [InlineData('{', '}')]
    [InlineData('"', '"')]
    [InlineData('\'', '\'')]
    [InlineData('x', '\0')]
    public void CloserFor(char open, char closer)
    {
        Assert.Equal(closer, AutoPair.CloserFor(open));
    }

    [Fact]
    public void PairAndSkipRules()
    {
        Assert.True(AutoPair.ShouldPair("", 0, '('));
        Assert.True(AutoPair.ShouldPair("f(", 2, '('));
        Assert.True(AutoPair.ShouldPair("", 0, '"'));
        Assert.False(AutoPair.ShouldPair("don't", 2, '\'')); // внутри слова — нет
        Assert.True(AutoPair.ShouldPair("say ", 4, '"'));
        Assert.True(AutoPair.ShouldSkip("()", 1, ')'));
        Assert.True(AutoPair.ShouldSkip("\"\"", 1, '"'));
        Assert.False(AutoPair.ShouldSkip("(a)", 1, ')'));
        Assert.False(AutoPair.ShouldSkip("", 0, '('));
        Assert.Equal(0, AutoPair.PairDeleteCol("()", 1));
        Assert.Null(AutoPair.PairDeleteCol("(a)", 1));
        Assert.Null(AutoPair.PairDeleteCol("(", 1));
    }

    [Fact]
    public void BufferPairOpsAreSingleUndo()
    {
        var buf = new TextBuffer(null);
        int v0 = buf.Version;
        (int r, int c) = buf.InsertPair(0, 0, '(', ')');
        Assert.Equal("()", buf.GetLine(0));
        Assert.Equal((0, 1), (r, c));
        Assert.Equal(v0 + 1, buf.Version);
        var del = buf.DeletePair(0, 1);
        Assert.NotNull(del);
        Assert.Equal("", buf.GetLine(0));
        Assert.Equal((0, 0), del.Value);
        Assert.Equal(v0 + 2, buf.Version);
        Assert.Null(buf.DeletePair(0, 0));
    }

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tui_pair_" + Guid.NewGuid().ToString("N"), "s.json")));
    }

    private static void Type(TuiEditor ed, char c) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false)]);

    private static string Line(TuiEditor ed)
    {
        var buf = (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
        return buf.GetLine(0);
    }

    private static int Col(TuiEditor ed) =>
        (int)typeof(TuiEditor).GetField("_col", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    [Fact]
    public void TypingPairsSkipsAndDeletes()
    {
        var ed = NewEditor();
        Type(ed, '(');
        Assert.Equal("()", Line(ed));
        Assert.Equal(1, Col(ed));
        Type(ed, ')'); // перепрыгнуть
        Assert.Equal("()", Line(ed));
        Assert.Equal(2, Col(ed));
    }

    [Fact]
    public void BackspaceDeletesPair()
    {
        var ed = NewEditor();
        Type(ed, '(');
        Assert.Equal("()", Line(ed));
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false)]);
        Assert.Equal("", Line(ed));
        Assert.Equal(0, Col(ed));
    }
}
