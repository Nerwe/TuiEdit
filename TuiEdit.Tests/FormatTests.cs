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
}
