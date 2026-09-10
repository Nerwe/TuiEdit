using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>
/// Held Ctrl+V (key auto-repeat, terminal paste bursts) must land as one buffer
/// mutation: otherwise repeats outrun the frame and keep inserting after release.
/// </summary>
public sealed class PasteCoalesceTests
{
    [Fact]
    public void HeldTerminalPasteMergesIntoSingleUndo()
    {
        var ed = NewEditor("x");
        try
        {
            InputReader.Parser.FeedEvent(new PasteInput("cd"));
            InputReader.Parser.FeedEvent(new KeyInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
            HandleInput(ed, new PasteInput("ab"));
            Assert.Equal("abcdx", ActiveBuf(ed).GetLine(0));
            // One mutation, not three: a single undo restores the original.
            Assert.True(ActiveBuf(ed).CanUndo);
            ActiveBuf(ed).Undo();
            Assert.Equal("x", ActiveBuf(ed).GetLine(0));
            Assert.False(ActiveBuf(ed).CanUndo);
            // The trailing non-paste event is stashed, not swallowed.
            object? held = Field(ed, "_heldEvent");
            Assert.IsType<KeyInput>(held);
            Assert.False(InputReader.Parser.HasQueued);
        }
        finally
        {
            InputReader.Parser.Clear();
        }
    }

    [Fact]
    public void HeldKeyPasteMergesClipboardRepeats()
    {
        var ed = NewEditor("x");
        try
        {
            Field(ed, "_clipboard")!
                .GetType().GetMethod("AddRange")!
                .Invoke(Field(ed, "_clipboard"), [new List<string> { "AB" }]);
            var paste = new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true);
            InputReader.Parser.FeedEvent(new KeyInput(paste));
            InputReader.Parser.FeedEvent(new KeyInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
            HandleKey(ed, paste);
            Assert.Equal("ABABx", ActiveBuf(ed).GetLine(0));
            ActiveBuf(ed).Undo();
            Assert.Equal("x", ActiveBuf(ed).GetLine(0));
            Assert.False(ActiveBuf(ed).CanUndo);
            Assert.IsType<KeyInput>(Field(ed, "_heldEvent"));
            Assert.False(InputReader.Parser.HasQueued);
        }
        finally
        {
            InputReader.Parser.Clear();
        }
    }

    [Fact]
    public void HeldKeyPasteMergesMultilineExactlyLikeSequential()
    {
        var ed = NewEditor("x");
        try
        {
            Field(ed, "_clipboard")!
                .GetType().GetMethod("AddRange")!
                .Invoke(Field(ed, "_clipboard"), [new List<string> { "L1", "L2" }]);
            var paste = new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true);
            InputReader.Parser.FeedEvent(new KeyInput(paste));
            HandleKey(ed, paste);
            // Sequential PasteLines twice would give the same three lines.
            Assert.Equal(new List<string> { "L1", "L2L1", "L2x" }, ActiveBuf(ed).Lines);
            ActiveBuf(ed).Undo();
            Assert.Equal(new List<string> { "x" }, ActiveBuf(ed).Lines);
            Assert.False(ActiveBuf(ed).CanUndo);
        }
        finally
        {
            InputReader.Parser.Clear();
        }
    }

    [Fact]
    public void FocusEventBehindPasteIsStashed()
    {
        var ed = NewEditor("x");
        try
        {
            InputReader.Parser.FeedEvent(new FocusInput(true));
            HandleInput(ed, new PasteInput("q"));
            Assert.Equal("qx", ActiveBuf(ed).GetLine(0));
            Assert.Equal(new FocusInput(true), Field(ed, "_heldEvent"));
        }
        finally
        {
            InputReader.Parser.Clear();
        }
    }

    [Fact]
    public void EmptyClipboardPasteDoesNotDrain()
    {
        var ed = NewEditor("x");
        try
        {
            InputReader.Parser.FeedEvent(new PasteInput("zz"));
            var paste = new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true);
            HandleKey(ed, paste); // empty clipboard: today's message path, queue untouched
            Assert.Equal("x", ActiveBuf(ed).GetLine(0));
            Assert.True(InputReader.Parser.HasQueued);
            Assert.Null(Field(ed, "_heldEvent"));
        }
        finally
        {
            InputReader.Parser.Clear();
        }
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        buf.TrySetEnding("lf");
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")), keys: KeyMap.Current);
    }

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static object? Field(TuiEditor ed, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed);

    private static void HandleInput(TuiEditor ed, InputEvent ev) =>
        typeof(TuiEditor).GetMethod("HandleInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [ev]);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);
}
