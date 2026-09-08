using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Перенос, сегменты и клавиши вида.</summary>
public sealed class WrapTests
{
    private static string J(List<int> xs) => string.Join(",", xs);

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    [Theory]
    [InlineData("hello", 10, "0")]
    [InlineData("0123456789", 10, "0")]
    [InlineData("0123456789A", 10, "0,10")]
    [InlineData("", 10, "0")]
    [InlineData("0123456789ABCDEF", 10, "0,10")]
    [InlineData("ab", 0, "0,1")]
    [InlineData("a\tb", 4, "0,4")]
    [InlineData("abcd\te", 5, "0,4")]
    [InlineData("ab\tc", 4, "0,4")]
    [InlineData("a\tbcdef", 4, "0,4,8")]
    [InlineData("ааа ббб", 5, "0,5")]
    public void SegmentStarts(string text, int width, string want)
    {
        Assert.Equal(want, J(WordWrap.SegmentStarts(text, width)));
    }

    [Fact]
    public void SegmentCount()
    {
        Assert.Equal(2, WordWrap.SegmentCount("0123456789A", 10));
        Assert.Equal(1, WordWrap.SegmentCount("", 10));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(100, 2)]
    public void SegmentAt(int col, int want)
    {
        Assert.Equal(want, WordWrap.SegmentAt(new List<int> { 0, 4, 9 }, col));
    }

    [Fact]
    public void CursorVisualRow()
    {
        var lines = new List<string> { "0123456789ABCDEF", "xy" };
        Assert.Equal(0, TuiEditor.CursorVisualRow(lines, 0, 0, 0, 0, 10, true));
        Assert.Equal(1, TuiEditor.CursorVisualRow(lines, 0, 0, 0, 12, 10, true));
        Assert.Equal(2, TuiEditor.CursorVisualRow(lines, 0, 0, 1, 0, 10, true));
        Assert.Equal(0, TuiEditor.CursorVisualRow(lines, 0, 1, 0, 12, 10, true));
        Assert.Equal(1, TuiEditor.CursorVisualRow(lines, 0, 1, 1, 1, 10, true));
        Assert.Equal(1, TuiEditor.CursorVisualRow(lines, 0, 0, 1, 1, 10, false));
        Assert.Equal(0, TuiEditor.CursorVisualRow(lines, 1, 0, 1, 1, 10, false));
        Assert.Equal(1, TuiEditor.CursorSeg("0123456789A", 10, 10));
        var deep = new List<string>
            { "0123456789ABCDEF", "0123456789ABCDEF", "0123456789ABCDEF", "0123456789ABCDEF", "xy" };
        Assert.Equal(8, TuiEditor.CursorVisualRow(deep, 0, 0, 4, 0, 10, true));
        Assert.True(TuiEditor.CursorVisualRow(deep, 0, 0, 4, 0, 10, true, 3) >= 3);
    }

    [Fact]
    public void ViewKeys()
    {
        Assert.Equal(EditorCommand.Help, KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false)));
        Assert.Equal(EditorCommand.ToggleLineNumbers, KeyMap.Map(K('n', ConsoleKey.N, alt: true)));
        Assert.Equal(EditorCommand.ToggleWrap, KeyMap.Map(K('z', ConsoleKey.Z, alt: true)));
        Assert.Equal(EditorCommand.OpenMenuFile, KeyMap.Map(K('f', ConsoleKey.F, alt: true)));
        Assert.Equal(EditorCommand.MoveLineUp,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, true, false)));
        Assert.Equal(EditorCommand.FindNext,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false)));
    }

    [Fact]
    public void ViewDefaults()
    {
        Assert.Equal(9, SettingsDialogState.RowCount);
        var s = new AppSettings();
        Assert.True(s.ShowLineNumbers);
        Assert.True(s.ShowIndentGuides);
        Assert.False(s.ShowWhitespace);
        Assert.False(s.WordWrap);
    }

    [Fact]
    public void AltPeriodMapsToToggleWhitespace()
    {
        Assert.Equal(EditorCommand.ToggleWhitespace,
            KeyMap.Map(new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, true, false)));
    }
}
