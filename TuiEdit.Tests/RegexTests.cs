using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Regex-поиск и замена.</summary>
public sealed class RegexTests
{
    private static TextBuffer Buf(params string[] lines)
    {
        string p = Path.GetTempFileName();
        File.WriteAllLines(p, lines);
        var b = new TextBuffer(null);
        b.Open(p);
        File.Delete(p);
        return b;
    }

    [Fact]
    public void RegexFind()
    {
        var b = Buf("foo 123 bar", "baz 456");
        var h = b.FindNext(@"\d+", 0, 0, true, false, true, true);
        Assert.True(h is { row: 0, col: 4, wrapped: false });
        var h2 = b.FindNext(@"\d+", 0, 5, true, false, true, true);
        Assert.True(h2 is { row: 1, col: 4, wrapped: false });
        var hw = b.FindNext(@"\d+", 1, 7, true, false, true, true);
        Assert.True(hw is { row: 0, col: 4, wrapped: true });
        var hp = b.FindPrev(@"\d+", 1, 4, true, false, true, true);
        Assert.True(hp is { row: 0, col: 4, wrapped: false });
        Assert.Equal(2, b.CountMatches(@"\d+", true, false, true));
        Assert.Equal(2, b.MatchOrdinal(@"\d+", 1, 4, true, false, true));
    }

    [Fact]
    public void RegexCaseAndWholeWord()
    {
        var bc = Buf("Foo foo");
        Assert.Equal(1, bc.CountMatches("foo", true, false, true));
        Assert.Equal(2, bc.CountMatches("foo", false, false, true));
        var bw = Buf("foobar foo foo.");
        Assert.Equal(2, bw.CountMatches("foo", true, true, true));
    }

    [Fact]
    public void RegexGroupsSingleUndo()
    {
        var bg = Buf("2024-01-15 done", "no date here");
        int n = bg.ReplaceAll(@"(\d{4})-(\d{2})-(\d{2})", "$3.$2.$1", 0, 0, true, false, true);
        Assert.Equal(1, n);
        Assert.Equal("15.01.2024 done", bg.GetLine(0));
        bg.Undo();
        Assert.Equal("2024-01-15 done", bg.GetLine(0));
        Assert.False(bg.CanUndo);
    }

    [Fact]
    public void BadPatternSafe()
    {
        Assert.True(TextBuffer.IsBadRegex("([", true, false));
        Assert.False(TextBuffer.IsBadRegex(@"\d+", true, false));
        Assert.False(TextBuffer.IsBadRegex("foo", true, false));
        var bb = Buf("foo 123");
        Assert.Null(bb.FindNext("([", 0, 0, true, false, true, true));
        Assert.Null(bb.FindPrev("([", 0, 0, true, false, true, true));
        Assert.Equal(0, bb.CountMatches("([", true, false, true));
        Assert.Equal(0, bb.ReplaceAll("([", "x", 0, 0, true, false, true));
        Assert.False(bb.CanUndo);
    }

    [Fact]
    public void CatastrophicBacktrackingTimeout()
    {
        var bt = Buf(new string('a', 32) + "!");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ht = bt.FindNext(@"^(a+)+$", 0, 0, true, false, true, true);
        sw.Stop();
        Assert.Null(ht);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void PlainSearchIntact()
    {
        var bb = Buf("foo 123");
        Assert.True(bb.FindNext("foo", 0, 0) is { row: 0, col: 0 });
        Assert.Equal(2, bb.ReplaceAll("o", "0", 0, 0, true, false, false));
    }

    [Fact]
    public void RegexDefaults()
    {
        Assert.Equal(7, SettingsDialogState.RowCount);
        Assert.False(new AppSettings().SearchUseRegex);
    }

    [Fact]
    public void ReplaceConfirmThreshold()
    {
        Assert.Equal(50, TuiEditor.ReplaceConfirmThreshold);
        Assert.False(TuiEditor.ShouldConfirmReplace(50));
        Assert.True(TuiEditor.ShouldConfirmReplace(51));
    }
}
