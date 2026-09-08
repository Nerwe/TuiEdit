using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class CliArgsTests
{
    [Theory]
    [InlineData("a.txt", "a.txt", 0)]
    [InlineData("a.txt:12", "a.txt", 12)]
    [InlineData("a.txt:$", "a.txt", int.MaxValue)]
    [InlineData("a.txt:", "a.txt:", 0)]
    [InlineData("a.txt:0", "a.txt:0", 0)]
    [InlineData("a.txt:x", "a.txt:x", 0)]
    [InlineData("C:\\d\\a.txt:3", "C:\\d\\a.txt", 3)]
    [InlineData("C:", "C:", 0)]
    [InlineData("dir:name", "dir:name", 0)]
    public void SplitFileLine(string arg, string path, int line)
    {
        Assert.Equal((path, line), CliArgs.SplitFileLine(arg));
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
