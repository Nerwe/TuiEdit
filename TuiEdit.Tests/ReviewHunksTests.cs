using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Review hunks: range grouping, current-file priority, editor flow.</summary>
public sealed class ReviewHunksTests : IDisposable
{
    private readonly string _dir;

    public ReviewHunksTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void GroupRangesMergesContiguous()
    {
        var ranges = ReviewHunks.GroupRanges(new HashSet<int> { 5, 1, 2, 3, 10 });
        Assert.Equal([(1, 3), (5, 5), (10, 10)], ranges);
        Assert.Empty(ReviewHunks.GroupRanges(new HashSet<int>()));
    }

    [Fact]
    public void CollectPrefersCurrentFile()
    {
        var marks = new Dictionary<string, (HashSet<int>, HashSet<int>)>
        {
            ["b.cs"] = (new HashSet<int> { 0 }, new HashSet<int>()),
            ["a.cs"] = (new HashSet<int>(), new HashSet<int> { 4, 5 }),
        };
        var hunks = ReviewHunks.Collect(
            f => ((IReadOnlySet<int>)marks[f].Item1, marks[f].Item2), "a.cs", new[] { "b.cs", "a.cs" });
        Assert.Equal(2, hunks.Count);
        Assert.Equal("a.cs", hunks[0].File);
        Assert.Equal((4, 5), (hunks[0].StartRow, hunks[0].EndRow));
        Assert.Equal("b.cs", hunks[1].File);
    }

    [Fact]
    public void ReviewFlowListsRepoChanges()
    {
        if (!GitTools.HaveGit())
            return;
        string dir = GitTools.InitRepo(out string file);
        try
        {
            File.AppendAllText(file, "\ntwo");
            var buf = new TextBuffer(null);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine(Path.GetTempPath(), "tui_revcfg_" + Guid.NewGuid().ToString("N"), "s.json")));
            // Point the editor at the repo: open the file so StartDir() resolves inside it.
            buf.Open(file);
            ed.Dispatcher.Execute(EditorCommand.ReviewChanges,
                new ConsoleKeyInfo('\0', ConsoleKey.R, false, true, false));
            object? dlg = typeof(TuiEditor).GetField("_dialog",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(ed);
            Assert.NotNull(dlg);
            object? state = dlg.GetType().GetField("_state",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(dlg);
            Assert.NotNull(state);
            object? kind = state.GetType().GetProperty("Kind")!.GetValue(state);
            Assert.Equal("Review", kind!.ToString());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
