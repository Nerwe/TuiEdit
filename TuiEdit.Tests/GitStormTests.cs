using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Spawn storm guard: repeated stale reads while a query is in flight must share
// the single query instead of spawning (and cancelling) one git per call.
// Observed white-box: every trigger mints a fresh CancellationTokenSource,
// so the CTS identity must stay put across rapid stale reads.
[Trait("Category", "Integration")]
public sealed class GitStormTests
{
    [Fact]
    public async Task StaleReadsShareOneQuery()
    {
        if (!GitTools.HaveGit())
            return;
        string repo = GitTools.InitRepo(out string file);
        try
        {
            var svc = new GitService();

            svc.StatusSegment(file);
            svc.DiffMarks(file);
            svc.ChangedFiles(repo);
            object? statusCts = Cts(svc, "_statusCts");
            object? diffCts = Cts(svc, "_diffCts");
            object? changesCts = Cts(svc, "_changesCts");
            Assert.NotNull(statusCts);
            Assert.NotNull(diffCts);
            Assert.NotNull(changesCts);

            for (int i = 0; i < 20; i++)
            {
                svc.StatusSegment(file);
                svc.DiffMarks(file);
                svc.ChangedFiles(repo);
            }

            Assert.Same(statusCts, Cts(svc, "_statusCts"));
            Assert.Same(diffCts, Cts(svc, "_diffCts"));
            Assert.Same(changesCts, Cts(svc, "_changesCts"));

            // Completion clears the in-flight flags (no stuck suppression).
            // Bound covers the 10s git timeout with margin; typically ~100ms.
            for (int i = 0; i < 300 && (Flag(svc, "_statusRefreshing")
                || Flag(svc, "_diffRefreshing")
                || Flag(svc, "_changesRefreshing")); i++)
                await Task.Delay(50);
            Assert.False(Flag(svc, "_statusRefreshing"));
            Assert.False(Flag(svc, "_diffRefreshing"));
            Assert.False(Flag(svc, "_changesRefreshing"));
        }
        finally
        {
            try { Directory.Delete(repo, true); } catch { }
        }
    }

    private static object? Cts(GitService svc, string name) =>
        typeof(GitService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(svc);

    private static bool Flag(GitService svc, string name) =>
        (bool)typeof(GitService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(svc)!;
}
