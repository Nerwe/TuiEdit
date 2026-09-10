using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

[Trait("Category", "Integration")]
public sealed class PickerEditTests : IDisposable
{
    private readonly string _dir;

    public PickerEditTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_pkedit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private FilePickerState State(string initial = "") =>
        new(PickerMode.Open, _dir, initial);

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    [Fact]
    public void HiddenToggleAndSizes()
    {
        File.WriteAllText(Path.Combine(_dir, "vis.txt"), "12345");
        File.WriteAllText(Path.Combine(_dir, ".hid"), "x");
        var p = State();
        Assert.Contains(p.Entries, e => e.Name == ".hid");
        Assert.Equal(5, p.Entries.First(e => e.Name == "vis.txt").Size);
        p.ShowHidden = false;
        p.Refresh();
        Assert.DoesNotContain(p.Entries, e => e.Name == ".hid");
        Assert.Contains(p.Entries, e => e.Name == "vis.txt");
    }

    [Fact]
    public void RenameHighlightedFlow()
    {
        File.WriteAllText(Path.Combine(_dir, "old.txt"), "x");
        var p = State();
        p.MoveHighlight(1); // old.txt (after ..); name syncs
        Assert.Equal("old.txt", p.Name);
        p.EndName(true); // select all
        p.InsertName("new.txt");
        Assert.Equal("ok", p.RenameHighlighted());
        Assert.True(File.Exists(Path.Combine(_dir, "new.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "old.txt")));
    }

    [Fact]
    public void RenameHighlightedRejects()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "y");
        var p = State();
        Assert.Equal("empty", p.RenameHighlighted()); // .. highlighted
        p.MoveHighlight(1); // a.txt
        p.EndName(true);
        p.InsertName("b.txt");
        Assert.Equal("exists", p.RenameHighlighted());
        Assert.True(File.Exists(Path.Combine(_dir, "a.txt"))); // untouched
        p.EndName(true);
        p.InsertName("x/y");
        Assert.Equal("invalid", p.RenameHighlighted());
    }

    [Fact]
    public void DialogF2RenamesAndNotices()
    {
        File.WriteAllText(Path.Combine(_dir, "old.txt"), "x");
        var p = State();
        var dlg = new FileDialog(p, "Open", "Save");
        p.MoveHighlight(1);
        p.EndName(true);
        p.InsertName("new.txt");
        dlg.HandleKey(K('\0', ConsoleKey.F2));
        Assert.True(File.Exists(Path.Combine(_dir, "new.txt")));
        Assert.Null(p.NoticeKey);
        dlg.HandleKey(K('\0', ConsoleKey.F2)); // same name — silent ok
        Assert.Null(p.NoticeKey);
    }

    [Fact]
    public void DialogCtrlHTogglesHidden()
    {
        File.WriteAllText(Path.Combine(_dir, ".hid"), "x");
        var p = State();
        var dlg = new FileDialog(p, "Open", "Save");
        dlg.HandleKey(K('\0', ConsoleKey.H, ctrl: true));
        Assert.False(p.ShowHidden);
        Assert.DoesNotContain(p.Entries, e => e.Name == ".hid");
    }

    [Fact]
    public void DialogF7F8ConfirmFlow()
    {
        var p = State();
        var dlg = new FileDialog(p, "Open", "Save");
        p.InsertName("sub");
        dlg.HandleKey(K('\0', ConsoleKey.F7));
        Assert.True(Directory.Exists(Path.Combine(_dir, "sub")));
        Assert.False(dlg.Closed);
        p.MoveHighlight(1); // highlight on sub
        dlg.HandleKey(K('\0', ConsoleKey.F8));
        dlg.HandleKey(K('n', ConsoleKey.N)); // cancel — folder survives
        Assert.True(Directory.Exists(Path.Combine(_dir, "sub")));
        dlg.HandleKey(K('\0', ConsoleKey.F8));
        dlg.HandleKey(K('y', ConsoleKey.Y)); // confirm — deleted
        Assert.False(Directory.Exists(Path.Combine(_dir, "sub")));
        Assert.False(dlg.Closed);
    }

    [Fact]
    public void MkdirCreatesAndLists()
    {
        var p = State();
        p.InsertName("sub");
        Assert.Equal("ok", p.MakeDir(null));
        Assert.True(Directory.Exists(Path.Combine(_dir, "sub")));
        Assert.Contains(p.Entries, e => e.Name == "sub" && e.IsDir);
        Assert.Null(p.NoticeKey);
    }

    [Fact]
    public void MkdirEmptyAndExists()
    {
        var p = State();
        Assert.Equal("empty", p.MakeDir(null));
        Assert.Equal("empty", p.MakeDir("   "));
        Directory.CreateDirectory(Path.Combine(_dir, "taken"));
        p.InsertName("taken");
        Assert.Equal("exists", p.MakeDir(null));
        File.WriteAllText(Path.Combine(_dir, "f.txt"), "x");
        Assert.Equal("exists", p.MakeDir(Path.Combine(_dir, "f.txt")));
    }

    [Fact]
    public void DeleteFileAndRecursiveDir()
    {
        string sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(Path.Combine(sub, "inner"));
        string f = Path.Combine(sub, "a.txt");
        File.WriteAllText(f, "x");
        Assert.Equal(2, FilePickerState.CountItems(sub));
        Assert.Equal(1, FilePickerState.CountItems(f));
        Assert.Equal(0, FilePickerState.CountItems(Path.Combine(_dir, "ghost")));
        var p = State();
        Assert.True(p.DeletePath(f));
        Assert.False(File.Exists(f));
        Assert.True(p.DeletePath(sub));
        Assert.False(Directory.Exists(sub));
    }

    [Fact]
    public void DeleteTargetGuards()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        var p = State();
        Assert.Null(p.DeleteTarget()); // highlight on ..
        p.MoveHighlight(1);
        Assert.EndsWith("a.txt", p.DeleteTarget());
        var drives = new FilePickerState(PickerMode.Open, "", "");
        Assert.Null(drives.DeleteTarget());
    }
}
