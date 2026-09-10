using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Breadth-first walk: hidden dirs are pruned (never descended), denied dirs are
// skipped without aborting the walk, symlink cycles terminate, caps apply top-down.
[Trait("Category", "Integration")]
public sealed class FileIndexTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void CapAppliesTopDown()
    {
        string dir = tmp.NewDir("cap");
        File.WriteAllText(Path.Combine(dir, "root.txt"), "x");
        string sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "x");

        List<string> one = FileIndex.EnumerateFiles(dir, maxFiles: 1);
        Assert.Equal([Path.Combine(dir, "root.txt")], one);
    }

    [Fact]
    public void SymlinkCycleTerminates()
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

        List<string> files = FileIndex.EnumerateFiles(dir);
        Assert.Contains(Path.Combine(dir, "f.txt"), files);
    }

    [Fact]
    public void MissingRootIsEmpty()
    {
        Assert.Empty(FileIndex.EnumerateFiles(Path.Combine(tmp.Path, "nope")));
        Assert.Empty(FileIndex.EnumerateFiles("", maxFiles: 10));
        Assert.Empty(FileIndex.EnumerateFiles(tmp.Path, maxFiles: 0));
    }
}
