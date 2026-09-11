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
        int t = InputReader.MaxSilentDrain;
        Assert.False(InputReader.DrainBudgetExceeded(0));
        Assert.False(InputReader.DrainBudgetExceeded(t - 1));
        Assert.True(InputReader.DrainBudgetExceeded(t));
        Assert.True(InputReader.DrainBudgetExceeded(t + 1));
    }

    [Fact]
    public void DrainFloodLogNeverThrows()
    {
        var ex = Record.Exception(() => InputLog.DrainFlood(InputReader.MaxSilentDrain));
        Assert.Null(ex);
    }
}
