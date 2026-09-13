using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Quick-open ft: extension filter plus substring query.</summary>
public sealed class QuickOpenFilterTests
{
    private static readonly AppSettings Settings = new();
    private static readonly Loc En = Loc.Load("en");

    private static List<PaletteEntry> Files() =>
    [
        new FileEntry("/r/a.cs", "a.cs"),
        new FileEntry("/r/b.txt", "b.txt"),
        new FileEntry("/r/c.cs", "src/c.cs"),
    ];

    [Fact]
    public void FtFiltersByExtension()
    {
        var view = CommandPaletteDialog.ApplyFilter(Files(), "ft:cs", Settings, En);
        Assert.Equal(2, view.Count);
        Assert.All(view, e => Assert.Contains(".cs", e.Label(Settings, En)));
    }

    [Fact]
    public void FtWithQueryCombinesBoth()
    {
        var view = CommandPaletteDialog.ApplyFilter(Files(), "ft:cs src", Settings, En);
        Assert.Single(view);
        Assert.Contains("c.cs", view[0].Label(Settings, En));
    }

    [Fact]
    public void FtIsCaseInsensitiveAndDotOptional()
    {
        var view = CommandPaletteDialog.ApplyFilter(Files(), "FT:.CS", Settings, En);
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public void FtMultipleExtensionsOrTogether()
    {
        var view = CommandPaletteDialog.ApplyFilter(Files(), "ft:cs,txt", Settings, En);
        Assert.Equal(3, view.Count);
    }

    [Fact]
    public void BareQueryUnchanged()
    {
        var view = CommandPaletteDialog.ApplyFilter(Files(), "b.t", Settings, En);
        Assert.Single(view);
    }

    [Fact]
    public void FtIgnoredForNonFileEntries()
    {
        List<PaletteEntry> all = CommandPaletteDialog.AllEntries(Settings, En);
        var view = CommandPaletteDialog.ApplyFilter(all, "ft:cs copy", Settings, En);
        Assert.Contains(view, e => e is CommandEntry c && c.Command == EditorCommand.CopyLine);
    }
}
