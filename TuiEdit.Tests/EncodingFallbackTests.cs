using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>File-open encoding fallback: BOM, strict UTF-8, then Latin1.</summary>
public sealed class EncodingFallbackTests
{
    [Fact]
    public void InvalidUtf8FallsBackToLatin1()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0x48, 0x69, 0xFF, 0x41]);
            var buf = new TextBuffer(path);
            Assert.Equal("Latin1", buf.EncodingLabel);
            Assert.Contains("ÿ", buf.GetLine(0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidUtf8StaysUtf8()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, "hi €\n"u8.ToArray());
            var buf = new TextBuffer(path);
            Assert.Equal("UTF-8", buf.EncodingLabel);
            Assert.Contains("€", buf.GetLine(0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Latin1RoundtripsOnSave()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0x48, 0xFF]);
            var buf = new TextBuffer(path);
            Assert.Equal("Latin1", buf.EncodingLabel);
            buf.Save(path);
            byte[] raw = File.ReadAllBytes(path);
            Assert.Equal(0x48, raw[0]);
            Assert.Equal(0xFF, raw[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TrySetEncodingAcceptsLatin1()
    {
        var buf = new TextBuffer(null);
        Assert.True(buf.TrySetEncoding("latin1"));
        Assert.Equal("Latin1", buf.EncodingLabel);
    }
}
