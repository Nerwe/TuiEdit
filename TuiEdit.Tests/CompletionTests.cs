using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class CompletionTests
{
    private static TextBuffer Buf(params string[] lines)
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string>(lines));
        return b;
    }

    [Fact]
    public void CollectRanksExactCaseFirst()
    {
        var b = Buf("Console ConsoleWrite consoleLog con", "concat");
        var words = Completion.Collect(b, "con");
        Assert.Equal(new[] { "concat", "consoleLog", "Console", "ConsoleWrite" }, words);
    }

    [Fact]
    public void CollectSkipsShortAndDupes()
    {
        var b = Buf("foo foo FooBar foo");
        Assert.Equal(new[] { "foo", "FooBar" }, Completion.Collect(b, "fo"));
        Assert.Equal(new[] { "FooBar" }, Completion.Collect(b, "foo"));
        Assert.Empty(Completion.Collect(b, "foobar"));
        Assert.Empty(Completion.Collect(b, "zzz"));
    }

    [Fact]
    public void CompleteShapeAndKey()
    {
        var loc = Loc.Load("en");
        var m = ModalState.Complete(loc, new List<string> { "console", "concat" });
        Assert.Equal(ModalKind.Complete, m.Kind);
        Assert.False(m.Danger);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Throws<ArgumentException>(() => ModalState.Complete(loc, new List<string>()));
        Assert.Equal(EditorCommand.CompleteWord,
            KeyMap.Map(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true)));
    }
}
