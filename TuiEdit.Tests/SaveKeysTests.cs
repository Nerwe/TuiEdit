using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Save keys and raw input.</summary>
public sealed class SaveKeysTests
{
    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    [Fact]
    public void SaveKeys()
    {
        Assert.Equal(EditorCommand.Save, KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true)));
        Assert.Equal(EditorCommand.SaveAs,
            KeyMap.Map(new ConsoleKeyInfo('\x13', ConsoleKey.S, true, false, true)));
        Assert.Equal(EditorCommand.ToggleBookmark,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F2, false, false, false)));
        Assert.Equal(EditorCommand.NextBookmark,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F2, true, false, false)));
        Assert.Equal(EditorCommand.SaveAs, KeyMap.Map(K('\x0F', ConsoleKey.O, ctrl: true)));
    }

    [Fact]
    public void CommentSlashForms()
    {
        // The "/" key reports as Divide, Oem2, or bare 0x1F depending on terminal/layout.
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, true)));
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, true)));
        Assert.Equal(EditorCommand.ToggleComment,
            KeyMap.Map(new ConsoleKeyInfo('\x1F', ConsoleKey.Oem2, false, false, true)));
        // Bare Oem2 still types text.
        Assert.Equal(EditorCommand.InsertChar, KeyMap.Map(K('/', ConsoleKey.Oem2)));
    }

    [Fact]
    public void NeighbourKeysIntact()
    {
        Assert.Equal(EditorCommand.Quit, KeyMap.Map(K('\x11', ConsoleKey.Q, ctrl: true)));
        Assert.Equal(EditorCommand.SelectAll, KeyMap.Map(K('a', ConsoleKey.A, ctrl: true)));
        Assert.Equal(EditorCommand.Replace, KeyMap.Map(K('h', ConsoleKey.H, ctrl: true)));
        Assert.Equal(EditorCommand.FindNext,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false)));
        Assert.Equal(EditorCommand.Undo, KeyMap.Map(K('z', ConsoleKey.Z, ctrl: true)));
    }
}
