using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Async pipelines must honor cancellation: no hangs, no throws, no partial state.
public sealed class AsyncServiceTests
{
    [Fact]
    public async Task GitRunAsyncCancelledReturnsNull()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        string? output = await GitProcess.RunAsync(dir, cts.Token, "--version");
        Assert.Null(output);
    }

    [Fact]
    public async Task GitStatusRefreshCancelledStoresNothing()
    {
        var svc = new GitService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        string? seg = await svc.RefreshStatusAsync(Path.GetTempPath(), cts.Token);
        Assert.Null(seg);
    }

    [Fact]
    public async Task GitDiffRefreshCancelledReturnsEmpty()
    {
        var svc = new GitService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (added, modified) = await svc.RefreshDiffAsync(
            Path.Combine(Path.GetTempPath(), "nope.txt"), cts.Token);
        Assert.Empty(added);
        Assert.Empty(modified);
    }

    [Fact]
    public async Task HighlightPrefetchMatchesSync()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>
        {
            "// comment",
            "var s = \"text\"; // trailing",
            "int n = 42;",
        });
        CompiledGrammar? grammar = GrammarRegistry.ForExtension(".cs");
        Assert.NotNull(grammar);

        var svc = new HighlightService();
        await svc.PrefetchAsync(buf, grammar, CancellationToken.None);

        var fresh = new SyntaxHighlighter();
        for (int r = 0; r < buf.Count; r++)
            Assert.Equal(fresh.GetLine(buf, grammar, r), svc.GetLine(buf, grammar, r));
    }

    [Fact]
    public async Task HighlightPrefetchCancelledLeavesSyncUsable()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "int n = 42;" });
        CompiledGrammar? grammar = GrammarRegistry.ForExtension(".cs");

        var svc = new HighlightService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.PrefetchAsync(buf, grammar, cts.Token));

        // Sync path unaffected by the cancelled prefetch.
        Assert.NotEmpty(svc.GetLine(buf, grammar, 0));
    }
}
