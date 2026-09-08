using System.Text;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class FormatTests
{
    [Fact]
    public void EncodingCyclesUtf8BomUtf16()
    {
        var b = new TextBuffer(null);
        Assert.Equal("UTF-8", b.EncodingLabel);
        b.CycleEncoding();
        Assert.Equal("UTF-8 BOM", b.EncodingLabel);
        b.CycleEncoding();
        Assert.Equal("UTF-16 LE", b.EncodingLabel);
        b.CycleEncoding();
        Assert.Equal("UTF-8", b.EncodingLabel);
        Assert.True(b.IsModified);
    }

    [Fact]
    public void EndingCyclesAndAffectsSave()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "a", "b" });
        string p = Path.GetTempFileName();
        try
        {
            while (b.EndingLabel != "LF")
                b.CycleEnding();
            b.Save(p);
            Assert.Equal("a\nb\n", File.ReadAllText(p, Encoding.UTF8));
            while (b.EndingLabel != "CRLF")
                b.CycleEnding();
            b.Save(p);
            byte[] raw = File.ReadAllBytes(p);
            Assert.Equal("a\r\nb\r\n", Encoding.UTF8.GetString(raw));
        }
        finally
        {
            File.Delete(p);
        }
    }

    [Fact]
    public void CountStats()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "hello world", "", "a_b c!" });
        Assert.Equal((3, 4, 17), b.CountStats());
    }

    [Fact]
    public void F4MapsToDocStats()
    {
        Assert.Equal(EditorCommand.DocStats,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F4, false, false, false)));
    }

    [Fact]
    public void SortLinesSingleUndo()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "pear", "Apple", "fig", "apple" });
        b.SortLines(0, 3);
        Assert.Equal(new[] { "Apple", "apple", "fig", "pear" }, b.Lines);
        b.Undo();
        Assert.Equal(new[] { "pear", "Apple", "fig", "apple" }, b.Lines);
        b.SortLines(1, 2);
        Assert.Equal(new[] { "pear", "Apple", "fig", "apple" }, b.Lines);
    }

    [Fact]
    public void TrimTrailingSingleUndo()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "a  ", "\t", "b\t ", "c" });
        Assert.Equal(3, b.TrimTrailingWhitespace());
        Assert.Equal(new[] { "a", "", "b", "c" }, b.Lines);
        b.Undo();
        Assert.Equal(new[] { "a  ", "\t", "b\t ", "c" }, b.Lines);
        Assert.Equal(3, b.TrimTrailingWhitespace());
        Assert.Equal(0, b.TrimTrailingWhitespace());
    }

    [Fact]
    public void IndentCyclesBothWaysWithoutDirty()
    {
        var b = new TextBuffer(null);
        Assert.Equal("    ", b.IndentString);
        b.CycleIndent();
        Assert.Equal("  ", b.IndentString);
        Assert.Equal("2sp", b.IndentLabel);
        b.CycleIndent();
        Assert.Equal("\t", b.IndentString);
        Assert.Equal("Tab", b.IndentLabel);
        b.CycleIndent(-1);
        Assert.Equal("  ", b.IndentString);
        Assert.False(b.IsModified);
    }

    [Fact]
    public void FormatDialogCyclesBuffer()
    {
        var b = new TextBuffer(null);
        var dlg = new FormatDialog(b);
        Assert.False(dlg.Closed);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal("UTF-8 BOM", b.EncodingLabel);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        Assert.Equal("UTF-8", b.EncodingLabel);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal("LF", b.EndingLabel);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal("  ", b.IndentString);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        Assert.Equal("    ", b.IndentString);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.True(dlg.Closed);
    }

    [Fact]
    public void F9MapsToFileFormat()
    {
        Assert.Equal(EditorCommand.FileFormat,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F9, false, false, false)));
    }
}
