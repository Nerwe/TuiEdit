using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Textual go-to-definition: ranking, same-file priority, skips.</summary>
public sealed class DefinitionFinderTests : IDisposable
{
    private readonly string _dir;

    public DefinitionFinderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_def_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Write(string name, params string[] lines)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllLines(p, lines);
        return p;
    }

    [Fact]
    public void KeywordDefinitionOutranksCallSite()
    {
        var lines = new List<string> { "foo(1)", "def foo(x):", "foo(2)" };
        var hits = DefinitionFinder.FindInLines(lines, "a.py", "foo");
        Assert.Equal((1, 4), (hits[0].Row, hits[0].Col));
    }

    [Fact]
    public void SkipsCaretPosition()
    {
        var lines = new List<string> { "foo(1)" };
        Assert.Empty(DefinitionFinder.FindInLines(lines, "a.py", "foo", skipRow: 0, skipCol: 0));
        Assert.Single(DefinitionFinder.FindInLines(lines, "a.py", "foo"));
    }

    [Fact]
    public void AssignmentAndCallForms()
    {
        Assert.Equal((0, 0), Value(DefinitionFinder.FindInLines(new List<string> { "count = 0" }, "a.py", "count")));
        Assert.Equal((0, 0), Value(DefinitionFinder.FindInLines(new List<string> { "count(1)" }, "a.py", "count")));
    }

    private static (int, int) Value(List<DefHit> hits) => (hits[0].Row, hits[0].Col);

    [Fact]
    public void CurrentFileFirstThenProject()
    {
        string other = Write("b.py", "def foo(x):", "    return x");
        var lines = new List<string> { "x = foo(1)" };
        var hits = DefinitionFinder.Find(_dir, Path.Combine(_dir, "a.py"), lines, "foo", 0, 0);
        Assert.Equal(2, hits.Count);
        Assert.Equal("a.py", Path.GetFileName(hits[0].File)); // same-file call site first
        Assert.Equal(4, hits[0].Col);
        Assert.Equal(other, hits[1].File);
        Assert.Equal(0, hits[1].Row);
    }

    [Fact]
    public void BinaryAndMissingSkipped()
    {
        string bin = Path.Combine(_dir, "x.bin");
        File.WriteAllBytes(bin, new byte[] { 0, 1, 2, 3 });
        Assert.Null(DefinitionFinder.ReadSafeLines(bin));
        Assert.Null(DefinitionFinder.ReadSafeLines(Path.Combine(_dir, "nope.py")));
    }
}
