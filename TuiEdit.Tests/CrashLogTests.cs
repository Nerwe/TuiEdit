using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Crash reports must never fail the reporter: temp dir is always writable,
// evil tags degrade to null, history is capped.
[Trait("Category", "Integration")]
public sealed class CrashLogTests
{
    [Fact]
    public void WriteCreatesReadableReport()
    {
        var ex = new InvalidOperationException("boom");
        string? path = CrashLog.Write("test", ex);
        Assert.NotNull(path);
        Assert.Equal(".log", Path.GetExtension(path));
        string content = File.ReadAllText(path);
        Assert.Contains("[test]", content, StringComparison.Ordinal);
        Assert.Contains("boom", content, StringComparison.Ordinal);
        File.Delete(path);
    }

    [Fact]
    public void WriteNeverThrowsOnEvilTag()
    {
        var ex = new InvalidOperationException("boom");
        var exception = Record.Exception(() => CrashLog.Write("../../evil", ex));
        Assert.Null(exception);
    }

    [Fact]
    public void PruneKeepsCap()
    {
        var ex = new InvalidOperationException("boom");
        for (int i = 0; i < 25; i++)
            CrashLog.Write("prune", ex);
        int count = Directory.GetFiles(CrashLog.Dir, "crash-*.log").Length;
        Assert.True(count <= 20, $"kept {count} reports");
    }
}
