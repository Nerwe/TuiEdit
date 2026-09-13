using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Time-undo: steps and age travel over timestamped snapshots.</summary>
public sealed class TimeUndoTests : IDisposable
{
    private readonly Func<DateTime> _prevClock;
    private DateTime _now;

    public TimeUndoTests()
    {
        _prevClock = TextBuffer.UtcNow;
        _now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        TextBuffer.UtcNow = () => _now;
    }

    public void Dispose()
    {
        TextBuffer.UtcNow = _prevClock;
        GC.SuppressFinalize(this);
    }

    private TextBuffer BufferWithEdits()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(["a"]);
        _now += TimeSpan.FromMinutes(10);
        buf.InsertChar(0, 1, 'b'); // "ab" at 12:10
        _now += TimeSpan.FromMinutes(10);
        buf.InsertChar(0, 2, 'c'); // "abc" at 12:20
        return buf;
    }

    [Fact]
    public void UndoStepsAndRedoStepsRoundtrip()
    {
        var buf = BufferWithEdits();
        Assert.Equal("abc", buf.GetLine(0));
        Assert.Equal(2, buf.UndoSteps(2));
        Assert.Equal("a", buf.GetLine(0));
        Assert.Equal(1, buf.RedoSteps(1));
        Assert.Equal("ab", buf.GetLine(0));
    }

    [Fact]
    public void UndoToAgeUndoesOnlyRecentEdits()
    {
        var buf = BufferWithEdits(); // now 12:20
        Assert.Equal(1, buf.UndoToAge(TimeSpan.FromMinutes(5)));
        Assert.Equal("ab", buf.GetLine(0));
    }

    [Fact]
    public void EarlierThenLaterAgeMovesForward()
    {
        var buf = BufferWithEdits(); // now 12:20, edits at 12:10 and 12:20
        Assert.Equal(2, buf.UndoToAge(TimeSpan.FromMinutes(30)));
        Assert.Equal("a", buf.GetLine(0));
        Assert.Equal(0, buf.RedoToAge(TimeSpan.FromMinutes(15))); // nothing older than 12:05
        Assert.Equal("a", buf.GetLine(0));
        Assert.Equal(1, buf.RedoToAge(TimeSpan.FromMinutes(10))); // redoes the 12:10 edit
        Assert.Equal("ab", buf.GetLine(0));
        Assert.Equal(1, buf.RedoToAge(TimeSpan.Zero));
        Assert.Equal("abc", buf.GetLine(0));
    }

    [Fact]
    public void NegativeAgeIsNoop()
    {
        var buf = BufferWithEdits();
        Assert.Equal(0, buf.UndoToAge(TimeSpan.FromMinutes(-1)));
        Assert.Equal(0, buf.RedoToAge(TimeSpan.FromMinutes(-1)));
        Assert.Equal("abc", buf.GetLine(0));
    }

    [Theory]
    [InlineData("earlier", 1, false)]
    [InlineData("earlier 3", 3, false)]
    [InlineData("later 2", 2, false)]
    public void ParseSteps(string text, int steps, bool hasAge)
    {
        CommandLineOp? op = CommandLine.Parse(text);
        Assert.NotNull(op);
        Assert.False(hasAge);
        if (op is CommandLineOp.Earlier e)
            Assert.Equal(steps, e.Steps);
        else if (op is CommandLineOp.Later l)
            Assert.Equal(steps, l.Steps);
        else
            Assert.Fail("expected Earlier/Later");
    }

    [Theory]
    [InlineData("earlier 5m", 300)]
    [InlineData("later 30s", 30)]
    [InlineData("earlier 2h", 7200)]
    public void ParseAge(string text, int totalSeconds)
    {
        CommandLineOp? op = CommandLine.Parse(text);
        Assert.NotNull(op);
        TimeSpan? age = op switch
        {
            CommandLineOp.Earlier e => e.Age,
            CommandLineOp.Later l => l.Age,
            _ => null,
        };
        Assert.NotNull(age);
        Assert.Equal(totalSeconds, (int)age.Value.TotalSeconds);
    }

    [Theory]
    [InlineData("earlier -1")]
    [InlineData("later xyz")]
    [InlineData("earlier 5x")]
    public void ParseGarbageIsNull(string text)
    {
        Assert.Null(CommandLine.Parse(text));
    }
}
