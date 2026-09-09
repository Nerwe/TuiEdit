using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class CliArgsTests
{
    [Theory]
    [InlineData("a.txt", "a.txt", 0, 0)]
    [InlineData("a.txt:12", "a.txt", 12, 0)]
    [InlineData("a.txt:12:8", "a.txt", 12, 8)]
    [InlineData("a.txt:$", "a.txt", int.MaxValue, 0)]
    [InlineData("a.txt:", "a.txt:", 0, 0)]
    [InlineData("a.txt:0", "a.txt:0", 0, 0)]
    [InlineData("a.txt:x", "a.txt:x", 0, 0)]
    [InlineData("a.txt:12:x", "a.txt:12:x", 0, 0)]
    [InlineData("C:\\d\\a.txt:3", "C:\\d\\a.txt", 3, 0)]
    [InlineData("C:\\d\\a.txt:3:5", "C:\\d\\a.txt", 3, 5)]
    [InlineData("C:", "C:", 0, 0)]
    [InlineData("dir:name", "dir:name", 0, 0)]
    public void SplitFileLine(string arg, string path, int line, int col)
    {
        Assert.Equal((path, line, col), CliArgs.SplitFileLine(arg));
    }

    [Theory]
    [InlineData("120", 120, 0)]
    [InlineData("120:8", 120, 8)]
    [InlineData("$", int.MaxValue, 0)]
    [InlineData("x", -1, -1)]
    [InlineData("0", -1, -1)]
    [InlineData("12:0", -1, -1)]
    public void ParseGoTo(string s, int line, int col)
    {
        var r = TuiEditor.ParseGoTo(s);
        if (line < 0)
        {
            Assert.Null(r);
            return;
        }
        Assert.NotNull(r);
        Assert.Equal((line, col), r.Value);
    }

    [Fact]
    public void GoToLineNumberClamps()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "a", "b", "c" });
        var ed = new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.GoToLineNumber(2);
        Assert.Equal(1, Row(ed));
        ed.GoToLineNumber(99);
        Assert.Equal(2, Row(ed));
        ed.GoToLineNumber(int.MaxValue);
        Assert.Equal(2, Row(ed));
    }

    private static int Row(TuiEditor ed) =>
        (int)typeof(TuiEditor).GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
}
