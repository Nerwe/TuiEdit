using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class CommentToggleTests
{
    private static TextBuffer Buf(params string[] lines)
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string>(lines));
        return b;
    }

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    [Fact]
    public void CommentAddsMarkerAfterIndent()
    {
        var b = Buf("int x;", "    int y;", "");
        int[] d = b.ToggleLineComment(0, 2, "//");
        Assert.Equal(new[] { "// int x;", "    // int y;", "// " }, b.Lines);
        Assert.Equal(new[] { 3, 3, 3 }, d);
    }

    [Fact]
    public void UncommentRemovesMarkerAndSpace()
    {
        var b = Buf("// int x;", "    //int y;");
        int[] d = b.ToggleLineComment(0, 1, "//");
        Assert.Equal(new[] { "int x;", "    int y;" }, b.Lines);
        Assert.Equal(new[] { -3, -2 }, d);
    }

    [Fact]
    public void MixedRangeCommentsAll()
    {
        var b = Buf("// a;", "b;");
        b.ToggleLineComment(0, 1, "#");
        Assert.Equal(new[] { "# // a;", "# b;" }, b.Lines);
    }

    [Fact]
    public void ToggleRoundTripAndSingleUndo()
    {
        var b = Buf("a;", "b;");
        b.ToggleLineComment(0, 1, "#");
        b.ToggleLineComment(0, 1, "#");
        Assert.Equal(new[] { "a;", "b;" }, b.Lines);
        b.Undo();
        Assert.Equal(new[] { "# a;", "# b;" }, b.Lines);
    }

    [Fact]
    public void HashCommentForPython()
    {
        var b = Buf("x = 1");
        b.ToggleLineComment(0, 0, "#");
        Assert.Equal(new[] { "# x = 1" }, b.Lines);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void NoSelectionCommentsOnlyCursorLine()
    {
        // Stale (0,0) anchor must not widen the range to line 0.
        string dir = Path.Combine(Path.GetTempPath(), "tui_cmt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "t.cs");
            File.WriteAllLines(file, ["int a;", "int b;", "int c;"]);
            var buf = new TextBuffer(null);
            buf.Open(file);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine("x", "settings.json")));
            ed.GoToLineNumber(2);
            HandleKey(ed, new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, true));
            Assert.Equal(["int a;", "// int b;", "int c;", ""], buf.Lines);
            HandleKey(ed, new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, true));
            Assert.Equal(["int a;", "int b;", "int c;", ""], buf.Lines);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void CtrlSlashMapsToToggleComment()
    {
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, true)));
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('\x1F', ConsoleKey.Oem2, false, false, true)));
    }
}
