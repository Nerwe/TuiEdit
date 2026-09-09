using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Binding overrides on top of the default table.</summary>
public sealed class KeyMapOverrideTests
{
    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    [Fact]
    public void ReplaceMovesCommandToNewKey()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?> { ["Save"] = "Ctrl+Shift+S" });
            Assert.Equal(EditorCommand.Save,
                KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true, shift: true)));
            Assert.Equal(EditorCommand.None, // old key no longer saves
                KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true)));
            Assert.Equal("Ctrl+Shift+S", KeyMap.HintFor(EditorCommand.Save));
        }
        finally { KeyMap.ResetToDefaults(); }
    }

    [Fact]
    public void NullUnbindsAndHidesHint()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?> { ["Save"] = null });
            Assert.Equal(EditorCommand.None, KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true)));
            Assert.Equal(string.Empty, KeyMap.HintFor(EditorCommand.Save));
            // The rest stays alive.
            Assert.Equal(EditorCommand.Quit, KeyMap.Map(K('\x11', ConsoleKey.Q, ctrl: true)));
        }
        finally { KeyMap.ResetToDefaults(); }
    }

    [Fact]
    public void LastWinsOnConflict()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?>
            {
                ["Save"] = "Ctrl+G",
                ["GoToLine"] = "Ctrl+G", // steals the key from Save
            });
            Assert.Equal(EditorCommand.GoToLine, KeyMap.Map(K('\x07', ConsoleKey.G, ctrl: true)));
        }
        finally { KeyMap.ResetToDefaults(); }
    }

    [Fact]
    public void KeylessCommandBecomesBindable()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?> { ["SaveAll"] = "Ctrl+T" });
            Assert.Equal(EditorCommand.SaveAll, KeyMap.Map(K('\x14', ConsoleKey.T, ctrl: true)));
            Assert.Equal("^T", KeyMap.HintFor(EditorCommand.SaveAll));
        }
        finally { KeyMap.ResetToDefaults(); }
    }

    [Fact]
    public void BadEntriesIgnoredAndDefaultsKept()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?>
            {
                ["Nope"] = "Ctrl+S", // no such command
                ["Save"] = "Win+S", // garbage in the notation
            });
            Assert.Equal(EditorCommand.Save, KeyMap.Map(K('\x13', ConsoleKey.S, ctrl: true)));
            Assert.Null(KeyMap.HintFor(EditorCommand.Save)); // no override — menu literal
        }
        finally { KeyMap.ResetToDefaults(); }
    }

    [Fact]
    public void TypingFallbackSurvivesOverrides()
    {
        try
        {
            KeyMap.SetOverrides(new Dictionary<string, string?> { ["Save"] = "F12" });
            Assert.Equal(EditorCommand.InsertChar, KeyMap.Map(K('a', ConsoleKey.A)));
            Assert.Equal(EditorCommand.None, KeyMap.Map(K('\0', ConsoleKey.J, ctrl: true)));
        }
        finally { KeyMap.ResetToDefaults(); }
    }
}
