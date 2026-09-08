using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Клавиши сохранения и сырой ввод.</summary>
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
        Assert.Equal(EditorCommand.None,
            KeyMap.Map(new ConsoleKeyInfo('\0', ConsoleKey.F2, false, false, false)));
        Assert.Equal(EditorCommand.SaveAs, KeyMap.Map(K('\x0F', ConsoleKey.O, ctrl: true)));
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

    [Fact]
    public void TerminalNoThrow()
    {
        bool restoredOk = false;
        try
        {
            Terminal.TryEnableRawInput();
            Terminal.RestoreInput();
            Terminal.RestoreInput(); // идемпотентность
            restoredOk = true;
        }
        catch (Exception ex)
        {
            Assert.Fail("terminal threw: " + ex.GetType().Name);
        }
        Assert.True(restoredOk);
    }
}
