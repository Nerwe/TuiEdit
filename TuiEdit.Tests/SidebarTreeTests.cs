using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Lazy tree: children load on expand, dirs first with natural sort,
// hidden entries kept (dimmed downstream), cycles refuse, mtime refreshes.
[Trait("Category", "Integration")]
public sealed class SidebarTreeTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void BuildsSortedDirsFirst()
    {
        string dir = tmp.NewDir("tree");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "a10.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "a2.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));

        var tree = new SidebarTree(dir);
        var rows = tree.VisibleRows();
        Assert.Equal("sub", rows[0].node.Name);
        Assert.True(rows[0].node.IsDir);
        Assert.Equal(["sub", "a2.txt", "a10.txt", "b.txt"], rows.Select(r => r.node.Name).ToList());
        Assert.All(rows, r => Assert.Equal(0, r.depth));
    }

    [Fact]
    public void ChildrenAreLazy()
    {
        string dir = tmp.NewDir("lazy");
        string sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "inner.txt"), "x");

        var tree = new SidebarTree(dir);
        SidebarNode sub0 = tree.VisibleRows()[0].node;
        Assert.Empty(sub0.Children);

        Assert.True(tree.Toggle(sub0));
        Assert.Equal(["inner.txt"], sub0.Children.Select(c => c.Name).ToList());
        var rows = tree.VisibleRows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[1].depth);

        Assert.False(tree.Toggle(sub0));
        Assert.Single(tree.VisibleRows());
    }

    [Fact]
    public void ToggleFileIsNoop()
    {
        string dir = tmp.NewDir("noop");
        File.WriteAllText(Path.Combine(dir, "f.txt"), "x");
        var tree = new SidebarTree(dir);
        Assert.False(tree.Toggle(tree.VisibleRows()[0].node));
    }

    [Fact]
    public void SymlinkCycleRefuses()
    {
        string dir = tmp.NewDir("cyc");
        File.WriteAllText(Path.Combine(dir, "f.txt"), "x");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(dir, "loop"), dir);
        }
        catch
        {
            return; // no symlink privilege — soft skip
        }

        var tree = new SidebarTree(dir);
        SidebarNode loop = tree.VisibleRows().First(r => r.node.Name == "loop").node;
        Assert.False(tree.Toggle(loop));
        Assert.Empty(loop.Children);
    }

    [Fact]
    public void RefreshPicksUpExternalChanges()
    {
        string dir = tmp.NewDir("refresh");
        var tree = new SidebarTree(dir);
        Assert.Empty(tree.VisibleRows());

        File.WriteAllText(Path.Combine(dir, "new.txt"), "x");
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddSeconds(2)); // mtime granularity
        Assert.True(tree.Refresh(tree.RootNode));
        Assert.Equal(["new.txt"], tree.VisibleRows().Select(r => r.node.Name).ToList());
        Assert.False(tree.Refresh(tree.RootNode)); // nothing changed since
    }

    [Fact]
    public void ExpansionSurvivesParentRefresh()
    {
        string dir = tmp.NewDir("survive");
        string sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "a.txt"), "x");
        var tree = new SidebarTree(dir);
        SidebarNode sub0 = tree.VisibleRows()[0].node;
        Assert.True(tree.Toggle(sub0));
        Assert.Equal(2, tree.VisibleRows().Count);

        File.WriteAllText(Path.Combine(dir, "top.txt"), "x");
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddSeconds(2)); // mtime granularity
        Assert.True(tree.RefreshExpanded());
        var rows = tree.VisibleRows();
        Assert.Contains(rows, r => r.node.Name == "top.txt");
        Assert.Contains(rows, r => r.node.Name == "a.txt" && r.depth == 1); // still expanded
    }

    [Fact]
    public void BrokenRootIsEmpty()
    {
        var tree = new SidebarTree(Path.Combine(tmp.Path, "nope"));
        Assert.Empty(tree.VisibleRows());
        var ex = Record.Exception(() => tree.Refresh(tree.RootNode));
        Assert.Null(ex);
    }

    [Fact]
    public void HiddenEntriesKept()
    {
        string dir = tmp.NewDir("hidden");
        File.WriteAllText(Path.Combine(dir, ".dot"), "x");
        var tree = new SidebarTree(dir);
        SidebarNode dot = Assert.Single(tree.VisibleRows()).node;
        Assert.True(dot.IsHidden);
    }
}
