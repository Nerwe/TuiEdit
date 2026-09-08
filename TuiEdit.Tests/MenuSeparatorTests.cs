using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class MenuSeparatorTests
{
    private static MenuState State() => new(new List<TopMenu>
    {
        new("Edit", 'E', new List<MenuItem>
        {
            new("Undo", 'U', "^Z", EditorCommand.Undo),
            MenuItem.Separator,
            new("Copy", 'C', "^C", EditorCommand.CopyLine),
            MenuItem.Separator,
        }),
    });

    [Fact]
    public void NavigationSkipsSeparators()
    {
        var m = State();
        m.Open(0);
        Assert.Equal(0, m.SelectedIndex);
        m.MoveDown();
        Assert.Equal(2, m.SelectedIndex);
        m.MoveDown();
        Assert.Equal(0, m.SelectedIndex);
        m.MoveUp();
        Assert.Equal(2, m.SelectedIndex);
    }

    [Fact]
    public void HotkeyIgnoresSeparators()
    {
        var m = State();
        m.Open(0);
        Assert.Null(m.FindItemByHotkey('\0'));
        Assert.Equal(EditorCommand.CopyLine, m.FindItemByHotkey('c')!.Command);
    }
}
