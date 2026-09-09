using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Натуральный порядок имён.</summary>
public sealed class NaturalSortTests
{
    [Theory]
    [InlineData("file2.txt", "file10.txt")]
    [InlineData("file02.txt", "file10.txt")]
    [InlineData("a1b", "a1c")]
    [InlineData("img9.png", "img10.png")]
    [InlineData("a", "a0")]
    public void OrdersNaturally(string first, string second)
    {
        Assert.True(NaturalSort.Compare(first, second) < 0, $"{first} < {second}");
        Assert.True(NaturalSort.Compare(second, first) > 0);
    }

    [Fact]
    public void DigitsBeforeLettersAndCaseTiebreak()
    {
        Assert.True(NaturalSort.Compare("2a", "ab") < 0);
        Assert.True(NaturalSort.Compare("Ab", "aB") != 0); // регистр — тайбрейк
        Assert.Equal(0, NaturalSort.Compare("same", "same"));
        Assert.True(NaturalSort.Compare(null, "x") < 0);
        Assert.True(NaturalSort.Compare("x", null) > 0);
    }

    [Fact]
    public void SortsFileList()
    {
        var names = new List<string> { "file10.txt", "file2.txt", "file1.txt", "File20.txt" };
        names.Sort(NaturalSort.Comparer);
        Assert.Equal(["file1.txt", "file2.txt", "file10.txt", "File20.txt"], names);
    }
}
