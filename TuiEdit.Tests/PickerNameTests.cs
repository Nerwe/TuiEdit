using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Поле имени менеджера: выделение, слова, навигация.</summary>
public sealed class PickerNameTests : IDisposable
{
    private readonly string _root;

    public PickerNameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tui_pn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private FilePickerState Save(string name) => new(PickerMode.Save, _root, name);
    private FilePickerState Open(string name = "") => new(PickerMode.Open, _root, name);

    [Fact]
    public void ShiftArrowsSelect()
    {
        var p = Save("hello.txt");
        Assert.False(p.HasNameSelection);
        p.MoveNameCursor(-4, select: true);
        Assert.True(p.HasNameSelection);
        p.GetNameSelection(out int a, out int b);
        Assert.Equal((5, 9), (a, b));
        Assert.Equal(5, p.NamePos);
        p.MoveNameCursor(2, select: true);
        p.GetNameSelection(out a, out b);
        Assert.Equal((7, 9), (a, b));
        p.MoveNameCursor(2, select: true);
        Assert.False(p.HasNameSelection);
        Assert.Equal(9, p.NamePos);
        p.MoveNameCursor(-1, select: false);
        Assert.False(p.HasNameSelection);
        Assert.Equal(8, p.NamePos);
    }

    [Fact]
    public void TypeOverSelection()
    {
        var p = Save("hello.txt");
        p.MoveNameCursor(-8, select: true);
        p.InsertName("i");
        Assert.Equal("hi", p.Name);
        Assert.Equal(2, p.NamePos);
        Assert.False(p.HasNameSelection);
    }

    [Fact]
    public void BackspaceDeleteWithSelection()
    {
        var p = Save("hello.txt");
        p.HomeName(false);
        p.EndName(true);
        string dirBefore = p.CurrentDir;
        p.Backspace();
        Assert.Equal("", p.Name);
        Assert.Equal(dirBefore, p.CurrentDir); // без UpDir

        p = Save("hello.txt");
        p.MoveNameCursor(-4, select: true);
        p.DeleteChar();
        Assert.Equal("hello", p.Name);
        Assert.Equal(5, p.NamePos);
    }

    [Fact]
    public void ShiftHomeEnd()
    {
        var p = Save("hello.txt");
        p.MoveNameCursor(-5, select: false);
        p.HomeName(true);
        p.GetNameSelection(out int a, out int b);
        Assert.Equal((0, 4), (a, b));
        p.EndName(false);
        Assert.False(p.HasNameSelection);
        Assert.Equal(9, p.NamePos);
        p.MoveNameCursor(-5, select: false);
        p.EndName(true);
        p.GetNameSelection(out a, out b);
        Assert.Equal((4, 9), (a, b));
    }

    [Fact]
    public void CtrlArrowWords()
    {
        var p = Save("my-file.txt");
        p.HomeName(false);
        p.MoveNameWord(1, select: false);
        Assert.Equal(2, p.NamePos);
        p.MoveNameWord(1, select: false);
        Assert.Equal(7, p.NamePos);
        p.EndName(false);
        p.MoveNameWord(-1, select: false);
        Assert.Equal(8, p.NamePos);
        p.HomeName(false);
        p.MoveNameWord(1, select: true);
        p.GetNameSelection(out int a, out int b);
        Assert.Equal((0, 2), (a, b));
    }

    [Fact]
    public void CtrlBackspaceWords()
    {
        var p = Save("my-file.txt");
        p.EndName(false);
        p.DeleteNameWord(-1);
        Assert.Equal("my-file.", p.Name);
        Assert.Equal(8, p.NamePos);
        p.HomeName(false);
        p.DeleteNameWord(1);
        Assert.Equal("-file.", p.Name);
        Assert.Equal(0, p.NamePos);

        p = Save("hello.txt");
        p.MoveNameCursor(-4, select: true);
        p.DeleteNameWord(-1);
        Assert.Equal("hello", p.Name);
    }

    [Fact]
    public void BackspaceAtZeroNoop()
    {
        var p = Save("ab");
        p.HomeName(false);
        string upBefore = p.CurrentDir;
        p.Backspace();
        Assert.Equal(upBefore, p.CurrentDir);
        Assert.Equal("ab", p.Name);
        Assert.Equal(0, p.NamePos);

        p = Open();
        p.Backspace();
        Assert.Equal(_root, p.CurrentDir);
        Assert.Equal("", p.Name);
        p.UpDir();
        Assert.NotEqual(_root, p.CurrentDir);
    }

    [Fact]
    public void EnterDirOnlyIntoDirs()
    {
        var p = Open();
        int subIdx = p.Entries.FindIndex(e => e.Name == "sub");
        Assert.True(subIdx >= 0);
        while (p.Selected != subIdx) p.MoveHighlight(subIdx > p.Selected ? 1 : -1);
        Assert.True(p.EnterDir());
        Assert.EndsWith("sub", p.CurrentDir);

        int fileIdx = p.Entries.FindIndex(e => e.Name == "a.txt");
        if (fileIdx < 0)
        {
            p.UpDir();
            fileIdx = p.Entries.FindIndex(e => e.Name == "a.txt");
        }
        while (p.Selected != fileIdx) p.MoveHighlight(fileIdx > p.Selected ? 1 : -1);
        string curBefore = p.CurrentDir;
        Assert.False(p.EnterDir());
        Assert.Equal(curBefore, p.CurrentDir);

        p = Open();
        Assert.True(p.EnterDir());
        Assert.True(p.CurrentDir != _root || p.Entries.Count == 0);
    }

    [Fact]
    public void NavigateClearsSelection()
    {
        var p = Save("ab.txt");
        p.MoveNameCursor(-1, select: true);
        Assert.True(p.HasNameSelection);
        p.NavigateTo(Path.Combine(_root, "sub"));
        Assert.False(p.HasNameSelection);
    }
}
