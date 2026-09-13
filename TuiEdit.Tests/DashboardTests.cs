using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Startup dashboard: layout, picking, dismissal, type-through.</summary>
public sealed class DashboardTests
{
    private static readonly Loc En = Loc.Load("en");

    private static DashboardDialog NewDialog(List<string>? recent = null, Action<string>? onPick = null) =>
        new("0.9.3", recent ?? ["C:\\work\\a.txt", "C:\\work\\b.txt"], onPick ?? (_ => { }));

    private static Screen Draw(DashboardDialog dlg, int w = 80, int h = 24)
    {
        var scr = new Screen();
        scr.Resize(w, h);
        dlg.Draw(scr, Themes.Get("dark"), En);
        return scr;
    }

    [Fact]
    public void ShowsVersionAndRecents()
    {
        var scr = Draw(NewDialog());
        List<string> rows = scr.Snapshot();
        Assert.Contains(rows, r => r.Contains("0.9.3"));
        Assert.Contains(rows, r => r.Contains("a.txt"));
        Assert.Contains(rows, r => r.Contains("b.txt"));
        Assert.Contains(rows, r => r.Contains(En["dashboard.recent"]));
    }

    [Fact]
    public void EmptyRecentShowsHint()
    {
        var scr = Draw(NewDialog([]));
        Assert.Contains(scr.Snapshot(), r => r.Contains(En["dashboard.empty"]));
    }

    [Fact]
    public void DigitPicksRecent()
    {
        string? picked = null;
        var dlg = NewDialog(onPick: p => picked = p);
        dlg.HandleKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        Assert.True(dlg.Closed);
        Assert.EndsWith("b.txt", picked);
        Assert.Equal("C:\\work\\b.txt", dlg.Picked);
    }

    [Fact]
    public void EnterPicksSelected()
    {
        string? picked = null;
        var dlg = NewDialog(onPick: p => picked = p);
        dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.True(dlg.Closed);
        Assert.EndsWith("b.txt", picked);
    }

    [Fact]
    public void EscDismissesWithoutPick()
    {
        bool called = false;
        var dlg = NewDialog(onPick: _ => called = true);
        dlg.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.True(dlg.Closed);
        Assert.False(called);
        Assert.Null(dlg.Picked);
        Assert.Null(dlg.DismissKey);
    }

    [Fact]
    public void PrintableTypesThrough()
    {
        var key = new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false);
        var dlg = NewDialog();
        dlg.HandleKey(key);
        Assert.True(dlg.Closed);
        Assert.NotNull(dlg.DismissKey);
        Assert.Equal(key, dlg.DismissKey!.Key);
    }

    [Fact]
    public void ControlKeysAreSwallowed()
    {
        var dlg = NewDialog();
        dlg.HandleKey(new ConsoleKeyInfo('\x13', ConsoleKey.S, false, false, true));
        Assert.False(dlg.Closed);
    }
}
