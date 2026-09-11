using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Terminal mode helpers: best-effort, never throw (headless CI included).</summary>
public sealed class TerminalTests
{
    [Fact]
    public void DiscardPendingInputNeverThrows()
    {
        var ex = Record.Exception(() => Terminal.DiscardPendingInput());
        Assert.Null(ex);
    }

    [Fact]
    public void TakeFailStreakTripsFlushAtThreshold()
    {
        int t = Terminal.TakeFailFlushThreshold;
        Assert.False(Terminal.TakeFailStreakTripsFlush(0));
        Assert.False(Terminal.TakeFailStreakTripsFlush(t - 1));
        Assert.True(Terminal.TakeFailStreakTripsFlush(t));
        Assert.True(Terminal.TakeFailStreakTripsFlush(t + 1));
    }

    [Fact]
    public void RecoverStuckInputNeverThrows()
    {
        var ex = Record.Exception(() => Terminal.RecoverStuckInput());
        Assert.Null(ex);
    }

    [Fact]
    public void StuckFlushLogNeverThrows()
    {
        var ex = Record.Exception(() => InputLog.StuckFlush(Terminal.TakeFailFlushThreshold));
        Assert.Null(ex);
    }

    [Fact]
    public void DrainBudgetTripsAtThreshold()
    {
        int t = Terminal.MaxSilentPoll;
        Assert.False(Terminal.SilentPollExceeded(0));
        Assert.False(Terminal.SilentPollExceeded(t - 1));
        Assert.True(Terminal.SilentPollExceeded(t));
        Assert.True(Terminal.SilentPollExceeded(t + 1));
    }

    [Fact]
    public void DrainFloodLogNeverThrows()
    {
        var ex = Record.Exception(() => InputLog.DrainFlood(Terminal.MaxSilentPoll));
        Assert.Null(ex);
    }

    [Fact]
    public void DrainFloodLogWithDetailNeverThrows()
    {
        var ex = Record.Exception(() => InputLog.DrainFlood(Terminal.MaxSilentPoll, "motion=1 junk=2 head=empty"));
        Assert.Null(ex);
    }

    [Fact]
    public void DropJunkBatchHeadlessReturnsZero()
    {
        Assert.Equal(0, Terminal.DropJunkBatch(0));
        Assert.Equal(0, Terminal.DropJunkBatch(-1));
    }

    [Fact]
    public void DropJunkBatchNeverThrows()
    {
        int dropped = 0;
        var ex = Record.Exception(() => dropped = Terminal.DropJunkBatch(128));
        Assert.Null(ex);
        Assert.True(dropped >= 0);
    }

    [Fact]
    public void Utf8CodePagesNeverThrow()
    {
        var ex = Record.Exception(() =>
        {
            Terminal.TryEnableUtf8();
            Terminal.RestoreCodePages();
            Terminal.RestoreCodePages(); // second restore is a no-op
        });
        Assert.Null(ex);
    }

    [Fact]
    public void IsSgrMouseTerminalMatchesWtSession()
    {
        bool expected = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"));
        Assert.Equal(expected, Terminal.IsSgrMouseTerminal());
    }

    [Fact]
    public void MouseSourceLogNeverThrows()
    {
        var ex = Record.Exception(() => InputLog.MouseSource("sgr"));
        Assert.Null(ex);
    }

    [Fact]
    public void ReadKeyWithRetryRecoversAfterBlips()
    {
        int calls = 0;
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        ConsoleKeyInfo got = InputReader.ReadKeyWithRetry(() =>
        {
            calls++;
            if (calls < 3)
                throw new InvalidOperationException("blip");
            return key;
        });
        Assert.Equal(key, got);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ReadKeyWithRetryGivesUpAfterBudget()
    {
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => InputReader.ReadKeyWithRetry(() =>
        {
            calls++;
            throw new InvalidOperationException("gone");
        }));
        Assert.Equal(InputReader.MaxConsoleReadRetries + 1, calls);
    }

    [Fact]
    public void VirtualTerminalProbeIsStable()
    {
        bool first = Terminal.IsVirtualTerminalSupported();
        Assert.Equal(first, Terminal.IsVirtualTerminalSupported());
    }

    [Fact]
    public void BracketedPasteTogglesNeverThrow()
    {
        var ex = Record.Exception(() =>
        {
            Terminal.EnableBracketedPaste();
            Terminal.DisableBracketedPaste();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void IsDeadKeyPressMatchesHeuristic()
    {
        Assert.True(InputParser.IsDeadKeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Oem3, false, false, false)));
        Assert.False(InputParser.IsDeadKeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Oem3, false, false, true)));
        Assert.False(InputParser.IsDeadKeyPress(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)));
        Assert.False(InputParser.IsDeadKeyPress(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false)));
        Assert.False(InputParser.IsDeadKeyPress(new ConsoleKeyInfo(';', ConsoleKey.Oem1, false, false, false)));
    }

    [Fact]
    public void ParserDropsDeadKeyBeforeRealKey()
    {
        var parser = new InputParser(() => false, () => throw new InvalidOperationException());
        parser.Feed(new ConsoleKeyInfo('\0', ConsoleKey.Oem3, false, false, false));
        parser.Feed(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        InputEvent? ev = parser.TryRead(false);
        var key = Assert.IsType<KeyInput>(ev);
        Assert.Equal('a', key.Key.KeyChar);
        Assert.Null(parser.TryRead(false));
    }

    [Fact]
    public void DescribeHeadNeverThrows()
    {
        string? head = null;
        var ex = Record.Exception(() => head = Terminal.DescribeHead());
        Assert.Null(ex);
        Assert.False(string.IsNullOrEmpty(head));
    }

    [Fact]
    public void SameRecordComparesFullUnion()
    {
        var a = new Terminal.InputRecord { EventType = Terminal.KEY_EVENT };
        a.KeyEvent.KeyDown = 1;
        a.KeyEvent.UnicodeChar = 'A';
        var b = a;
        Assert.True(Terminal.SameRecord(a, b));
        b.KeyEvent.UnicodeChar = 'B';
        Assert.False(Terminal.SameRecord(a, b));
        b = a;
        b.EventType = Terminal.MOUSE_EVENT;
        Assert.False(Terminal.SameRecord(a, b));
    }
}
