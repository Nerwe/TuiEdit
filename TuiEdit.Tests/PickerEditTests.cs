using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

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
        p.MoveHighlight(1); // подсветка на sub
        dlg.HandleKey(K('\0', ConsoleKey.F8));
        dlg.HandleKey(K('n', ConsoleKey.N)); // отмена — папка жива
        Assert.True(Directory.Exists(Path.Combine(_dir, "sub")));
        dlg.HandleKey(K('\0', ConsoleKey.F8));
        dlg.HandleKey(K('y', ConsoleKey.Y)); // confirm — удалена
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
        Assert.Null(p.DeleteTarget()); // подсветка на ..
        p.MoveHighlight(1);
        Assert.EndsWith("a.txt", p.DeleteTarget());
        var drives = new FilePickerState(PickerMode.Open, "", "");
        Assert.Null(drives.DeleteTarget());
    }
}
