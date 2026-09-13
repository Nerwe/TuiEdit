using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Status bar: line layout and right block.</summary>
public sealed class StatusBarTests
{
    [Fact]
    public void BuildRightFull()
    {
        Assert.Equal(" UTF-8 | LF | 4sp | a.txt | ⎇ main* | 2/3 | P1/2 ",
            StatusBar.BuildRight("UTF-8", "LF", "4sp", "a.txt", "⎇ main*", 1, 3, 0, 2));
    }

    [Fact]
    public void BuildRightMinimalOmitsOptional()
    {
        Assert.Equal(" UTF-8 | LF | 4sp | a.txt ",
            StatusBar.BuildRight("UTF-8", "LF", "4sp", "a.txt", null, 0, 1, 0, 1));
    }

    [Fact]
    public void BuildKeepsRightTailOnOverflow()
    {
        // The right block is always visible: on overflow — its tail.
        string right = StatusBar.BuildRight("UTF-8", "LF", "4sp", "a.txt", "⎇ main*", 0, 1, 0, 1);
        string line = StatusBar.Build(" 1,1", right, 20);
        Assert.Equal(20, line.Length);
        Assert.EndsWith("⎇ main* ", line);
    }

    [Fact]
    public void BuildPadsMiddle()
    {
        Assert.Equal(" L  R ", StatusBar.Build(" L", " R ", 6));
    }

    [Fact]
    public void ExpandKeepsLiteralsAndUnknownVerbs()
    {
        static string? Resolve(string v) => v == "a" ? "A" : null;
        Assert.Equal("x A $(b) $(unclosed", StatusBar.Expand("x $(a) $(b) $(unclosed", Resolve));
        Assert.Equal("", StatusBar.Expand("", Resolve));
        Assert.Equal("xx", StatusBar.Expand("$(a)$(a)", _ => "x"));
    }

    [Fact]
    public void ExpandResolvesEmptyToEmpty()
    {
        Assert.Equal("[]", StatusBar.Expand("[$(e)]", _ => ""));
    }

    [Fact]
    public void OptValueFormatsScalars()
    {
        var s = new AppSettings { WordWrap = true, RulerColumn = 80, Theme = "dark" };
        Assert.Equal("on", StatusBar.OptValue(s, "WordWrap"));
        Assert.Equal("off", StatusBar.OptValue(s, "ShowWhitespace"));
        Assert.Equal("80", StatusBar.OptValue(s, "RulerColumn"));
        Assert.Equal("dark", StatusBar.OptValue(s, "theme")); // case-insensitive
        Assert.Null(StatusBar.OptValue(s, "Nope"));
        Assert.Null(StatusBar.OptValue(s, "SessionTabs")); // lists are not scalars
    }

    [Fact]
    public void PillSpansFindBadges()
    {
        var spans = StatusBar.PillSpans("Ln 1/3 Col 1 [*]   UTF-8 | ⎇ main[*] ", "[read-only]");
        Assert.Equal([(13, 3), (33, 3)], spans);
    }

    [Fact]
    public void PillSpansFindReadonlyLabel()
    {
        var spans = StatusBar.PillSpans("a.txt [read-only] ", "[read-only]");
        Assert.Equal([(6, 11)], spans);
    }

    [Fact]
    public void PillSpansSkipCleanRows()
    {
        Assert.Empty(StatusBar.PillSpans("Ln 1/3 Col 1   UTF-8 | LF ", "[read-only]"));
        Assert.Empty(StatusBar.PillSpans("a [b", "[read-only]")); // truncated pill never matches
    }

    [Fact]
    public void BindVerbFindsDefaultBinding()
    {
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        Assert.Equal("^S", StatusBar.BindVerb(table, "Save"));
        Assert.Equal("^S", StatusBar.BindVerb(table, "save")); // case-insensitive
        Assert.Equal("", StatusBar.BindVerb(table, "NoSuchCommand"));
    }
}
