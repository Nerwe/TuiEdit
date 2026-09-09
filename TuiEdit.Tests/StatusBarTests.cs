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
}
