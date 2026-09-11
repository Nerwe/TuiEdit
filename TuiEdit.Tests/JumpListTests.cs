using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Back/forward jump history (jumplist).</summary>
public sealed class JumpListTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void BackForwardWalk()
    {
        var jumps = new JumpList();
        Assert.Null(jumps.Back(new JumpPosition("a", 0, 0)));
        Assert.Null(jumps.Forward());
        jumps.Push(new JumpPosition("a", 0, 0));
        jumps.Push(new JumpPosition("a", 5, 0));
        Assert.Equal(new JumpPosition("a", 5, 0), jumps.Back(new JumpPosition("a", 9, 9))); // tip saved, step to previous
        Assert.Equal(new JumpPosition("a", 0, 0), jumps.Back(new JumpPosition("a", 5, 0))); // mid-list: plain step
        Assert.Null(jumps.Back(new JumpPosition("a", 0, 0))); // oldest: nowhere further
        Assert.Equal(new JumpPosition("a", 5, 0), jumps.Forward());
        Assert.Equal(new JumpPosition("a", 9, 9), jumps.Forward());
        Assert.Null(jumps.Forward()); // tip: nowhere further
    }

    [Fact]
    public void ConsecutiveDuplicatesDrop()
    {
        var jumps = new JumpList();
        jumps.Push(new JumpPosition("a", 1, 1));
        jumps.Push(new JumpPosition("a", 1, 1));
        Assert.Equal(1, jumps.Count);
    }

    [Fact]
    public void NewPushTruncatesForwardBranch()
    {
        var jumps = new JumpList();
        jumps.Push(new JumpPosition("a", 0, 0));
        jumps.Push(new JumpPosition("a", 1, 0));
        jumps.Back(new JumpPosition("a", 1, 0));
        jumps.Push(new JumpPosition("a", 2, 0));
        Assert.Equal(new JumpPosition("a", 0, 0), jumps.Back(new JumpPosition("a", 2, 0)));
        Assert.Equal(new JumpPosition("a", 2, 0), jumps.Forward());
        Assert.Null(jumps.Forward());
    }

    [Fact]
    public void CapacityDropsOldest()
    {
        var jumps = new JumpList();
        for (int i = 0; i < JumpList.Capacity + 5; i++)
            jumps.Push(new JumpPosition("a", i, 0));
        Assert.Equal(JumpList.Capacity, jumps.Count);
        JumpPosition cur = new("a", 99, 0);
        JumpPosition? last = null;
        for (int i = 0; i < JumpList.Capacity - 1; i++)
        {
            last = jumps.Back(cur);
            Assert.NotNull(last);
            cur = last;
        }
        Assert.Equal(new JumpPosition("a", 6, 0), last); // rows 0-4 fell off
        Assert.Null(jumps.Back(cur));
    }

    [Fact]
    public void AltArrowsMapToJumps()
    {
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        Assert.Equal(EditorCommand.JumpBack,
            table.Map(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: false, alt: true, control: false)));
        Assert.Equal(EditorCommand.JumpForward,
            table.Map(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: true, control: false)));
    }

    [Fact]
    public void EditorBackForwardSameFile()
    {
        var ed = NewEditor("a", "b", "c", "d", "e");
        ed.GoToPosition(5, 1);
        ed.PushJump();
        ed.GoToPosition(1, 1);
        ed.JumpBack();
        Assert.Equal((4, 0), Cursor(ed));
        ed.JumpForward();
        Assert.Equal((0, 0), Cursor(ed));
    }

    [Fact]
    public void EditorBackPrefersOpenTab()
    {
        string dir = tmp.NewDir();
        string f1 = Path.Combine(dir, "one.txt"), f2 = Path.Combine(dir, "two.txt");
        File.WriteAllText(f1, "a1\na2\na3\n");
        File.WriteAllText(f2, "b1\nb2\n");
        var ed = NewEditor("scratch");
        ed.OpenStartupFile(f1, 0, 0);
        ed.OpenStartupFile(f2, 0, 0);
        ed.SwitchTab(1); // f1
        ed.GoToPosition(3, 1);
        ed.PushJump();
        ed.SwitchTab(2); // f2
        ed.JumpBack();
        Assert.Equal(f1, ActivePath(ed));
        Assert.Equal((2, 0), Cursor(ed));
        ed.JumpForward();
        Assert.Equal(f2, ActivePath(ed));
    }

    [Fact]
    public void EmptyListIsNoOp()
    {
        var ed = NewEditor("a");
        var ex = Record.Exception(() =>
        {
            ed.JumpBack();
            ed.JumpForward();
        });
        Assert.Null(ex);
        Assert.Equal((0, 0), Cursor(ed));
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
    }

    private static (int row, int col) Cursor(TuiEditor ed)
    {
        var t = typeof(TuiEditor);
        return (
            (int)t.GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!,
            (int)t.GetField("_col", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!);
    }

    private static string? ActivePath(TuiEditor ed)
    {
        object? buf = typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed);
        return (string?)buf!.GetType().GetProperty("FilePath")!.GetValue(buf);
    }
}
