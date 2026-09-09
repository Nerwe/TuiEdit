using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Guards the table-driven dispatcher: every command must resolve,
// unknown ones (None) must be ignored without throwing.
public sealed class CommandDispatchTests
{
    [Fact]
    public void EveryCommandExceptNoneResolves()
    {
        var ed = NewEditor();
        foreach (EditorCommand cmd in Enum.GetValues<EditorCommand>())
        {
            if (cmd == EditorCommand.None)
                Assert.False(ed.Dispatcher.CanDispatch(cmd));
            else
                Assert.True(ed.Dispatcher.CanDispatch(cmd), cmd.ToString());
        }
    }

    [Fact]
    public void NoneIsIgnored()
    {
        var ed = NewEditor();
        var ex = Record.Exception(() =>
            ed.Dispatcher.Execute(EditorCommand.None, new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
        Assert.Null(ex);
    }

    [Fact]
    public void DispatchMovesCursor()
    {
        var ed = NewEditor();
        ed.Dispatcher.Execute(EditorCommand.MoveDown, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal(1, Row(ed));
    }

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "a", "b" });
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
    }

    private static int Row(TuiEditor ed) =>
        (int)typeof(TuiEditor).GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
}
