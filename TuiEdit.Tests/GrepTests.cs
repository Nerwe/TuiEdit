using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

[Trait("Category", "Integration")]
public sealed class GrepTests : IDisposable
{
    private readonly string _dir;

    public GrepTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_grep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void PlainSearchFindsHits()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "hello\nworld\n");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "HELLO again\n");
        var hits = Grep.Search(_dir, "hello", matchCase: false, wholeWord: false, useRegex: false);
        Assert.Equal(2, hits.Count);
        Assert.Equal(0, hits[0].Row);
        Assert.Equal(0, hits[0].Col);
        Assert.Equal("hello", hits[0].Text);
    }

    [Fact]
    public void CaseWholeWordAndRegex()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "foobar foo bar\n");
        Assert.Single(Grep.Search(_dir, "foo", true, true, false));
        Assert.Equal(2, Grep.Search(_dir, "foo", true, false, false).Count);
        Assert.Equal(2, Grep.Search(_dir, "f.o", true, false, true).Count);
        Assert.Empty(Grep.Search(_dir, "(bad", true, false, true));
    }

    [Fact]
    public void HiddenBinaryAndMissingSkipped()
    {
        File.WriteAllText(Path.Combine(_dir, ".hid"), "secret\n");
        Directory.CreateDirectory(Path.Combine(_dir, ".hdir"));
        File.WriteAllText(Path.Combine(_dir, ".hdir", "x.txt"), "secret\n");
        File.WriteAllBytes(Path.Combine(_dir, "bin.dat"), new byte[] { (byte)'s', 0, (byte)'x' });
        File.WriteAllText(Path.Combine(_dir, "ok.txt"), "secret\n");
        var hits = Grep.Search(_dir, "secret", false, false, false);
        Assert.Single(hits);
        Assert.EndsWith("ok.txt", hits[0].File);
        Assert.Empty(Grep.Search(Path.Combine(_dir, "ghost"), "secret", false, false, false));
    }

    [Fact]
    public void SingleFileRootAndCap()
    {
        string f = Path.Combine(_dir, "a.txt");
        File.WriteAllLines(f, Enumerable.Repeat("hit", 10));
        Assert.Equal(10, Grep.Search(f, "hit", false, false, false).Count);
        Assert.Equal(3, Grep.Search(f, "hit", false, false, false, maxHits: 3).Count);
    }

    [Fact]
    public void GrepShapeAndKey()
    {
        var loc = Loc.Load("en");
        var hits = new List<GrepHit> { new("a.txt", 4, 2, "needle here") };
        var m = ModalState.Grep(loc, hits);
        Assert.Equal(ModalKind.Grep, m.Kind);
        Assert.False(m.Danger);
        Assert.Contains("5", m.Buttons[0].Label);
        Assert.Throws<ArgumentException>(() => ModalState.Grep(loc, new List<GrepHit>()));
        Assert.Equal(EditorCommand.Grep,
            KeyMap.Map(new ConsoleKeyInfo('F', ConsoleKey.F, true, false, true)));
    }
}
