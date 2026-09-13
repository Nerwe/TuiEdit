using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Light registers: named yanks plus one-shot paste.</summary>
public sealed class RegisterTests
{
    [Fact]
    public void YankFillsUnnamedAndLastYank()
    {
        var ed = NewEditor("aaa");
        ed.Dispatcher.Execute(EditorCommand.CopyLine, Key());
        Assert.Equal(["aaa"], ed.Registers['"']);
        Assert.Equal(["aaa"], ed.Registers['0']);
    }

    [Fact]
    public void ExplicitRegisterYankAndPaste()
    {
        var ed = NewEditor("aaa", "bbb");
        Assert.True(ed.PickRegister('a'));
        ed.Dispatcher.Execute(EditorCommand.CopyLine, Key());
        Assert.Equal(["aaa"], ed.Registers['a']);
        Assert.Null(ed.PendingRegister); // one-shot consumed
        MoveDown(ed);
        Assert.True(ed.PickRegister('a'));
        ed.Dispatcher.Execute(EditorCommand.Paste, Key());
        Assert.Equal("aaabbb", ActiveBuf(ed).GetLine(1));
    }

    [Fact]
    public void MissingRegisterPasteIsNoop()
    {
        var ed = NewEditor("aaa");
        Assert.True(ed.PickRegister('z'));
        ed.Dispatcher.Execute(EditorCommand.Paste, Key());
        Assert.Equal("aaa", ActiveBuf(ed).GetLine(0));
        Assert.Null(ed.PendingRegister);
    }

    [Fact]
    public void OtherCommandsClearPending()
    {
        var ed = NewEditor("aaa", "bbb");
        Assert.True(ed.PickRegister('a'));
        HandleKey(ed, new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Null(ed.PendingRegister);
    }

    [Fact]
    public void BadRegisterRejected()
    {
        var ed = NewEditor("aaa");
        Assert.False(ed.PickRegister('!'));
        Assert.False(ed.PickRegister(' '));
        Assert.Null(ed.PendingRegister);
    }

    [Fact]
    public void RegisterPickIsBoundAndDispatched()
    {
        Assert.Equal(EditorCommand.RegisterPick,
            KeyMap.Map(new ConsoleKeyInfo('\x18', ConsoleKey.X, false, false, true)));
        var ed = NewEditor("aaa");
        Assert.True(ed.Dispatcher.CanDispatch(EditorCommand.RegisterPick));
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        buf.TrySetEnding("lf");
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: KeyMap.Current);
    }

    private static ConsoleKeyInfo Key() =>
        new('\0', ConsoleKey.NoName, false, false, false);

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static void MoveDown(TuiEditor ed) =>
        typeof(TuiEditor).GetMethod("MoveDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, []);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);
}
