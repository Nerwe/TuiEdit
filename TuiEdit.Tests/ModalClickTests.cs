using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Клики по модалкам: кнопки срабатывают, мимо — игнор без закрытия.</summary>
public sealed class ModalClickTests
{
    private const int W = 80;
    private const int H = 24;

    private static Loc En() => Loc.Load("en");

    /// <summary>Прогнать клик по каждой клетке; вернуть нажатые индексы.</summary>
    private static HashSet<int> ScanFires(ModalState state)
    {
        var fired = new HashSet<int>();
        Loc loc = En();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var dlg = new ModalDialog(state, (_, o) => { fired.Add(o.Button); });
                dlg.HandleClick(x, y, W, H, loc);
            }
        return fired;
    }

    [Fact]
    public void HorizontalButtonsFire()
    {
        var loc = En();
        var st = ModalState.Overwrite(loc, "a.txt");
        HashSet<int> fired = ScanFires(st);
        Assert.Equal(new HashSet<int> { 0, 1 }, fired);
    }

    [Fact]
    public void VerticalListFires()
    {
        var loc = En();
        var st = ModalState.Recent(loc, new List<string> { "a.txt", "b.txt", "c.txt" });
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, ScanFires(st));
    }

    [Fact]
    public void ScrolledListFiresVisibleWindow()
    {
        var loc = En();
        var files = new List<string>();
        for (int i = 0; i < 12; i++)
            files.Add($"f{i}.txt");
        var st = ModalState.Recent(loc, files);
        var scroller = new ModalDialog(st, (_, _) => { });
        for (int i = 0; i < 7; i++)
            scroller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        HashSet<int> fired = ScanFires(st);
        Assert.Equal(5, fired.Count); // окно из 5 видимых
        Assert.Equal(4, fired.Max() - fired.Min());
    }

    [Fact]
    public void OutsideBoxIgnoredAndStaysOpen()
    {
        var loc = En();
        bool fired = false;
        var dlg = new ModalDialog(ModalState.Overwrite(loc, "a.txt"), (_, _) => { fired = true; });
        Assert.False(dlg.HandleClick(0, 0, W, H, loc));
        Assert.False(dlg.HandleClick(W - 1, H - 1, W, H, loc));
        Assert.False(fired);
        Assert.False(dlg.Closed);
    }

    [Fact]
    public void ScrollListMovesSelection()
    {
        var loc = En();
        var files = new List<string>();
        for (int i = 0; i < 12; i++)
            files.Add($"f{i}.txt");
        var st = ModalState.Recent(loc, files);
        var dlg = new ModalDialog(st, (_, _) => { });
        Assert.Equal(0, st.Selected);
        dlg.ScrollList(1);
        dlg.ScrollList(1);
        dlg.ScrollList(1);
        Assert.Equal(3, st.Selected);
        dlg.ScrollList(-1);
        Assert.Equal(2, st.Selected);
        Assert.False(dlg.Closed); // стрелки не закрывают
    }

    [Fact]
    public void ScrollListIgnoresHorizontal()
    {
        var loc = En();
        bool fired = false;
        var dlg = new ModalDialog(ModalState.Overwrite(loc, "a.txt"), (_, _) => { fired = true; });
        dlg.ScrollList(1);
        Assert.False(fired);
        Assert.False(dlg.Closed);
    }

    [Fact]
    public void HoverHighlightsButtonPixels()
    {
        var loc = En();
        var theme = Themes.Get("dark");
        var scr = new Screen();
        scr.Resize(W, H);
        var dlg = new ModalDialog(ModalState.Overwrite(loc, "a.txt"), (_, _) => { });
        dlg.HoverActive = true;
        dlg.HoverButton = 1;
        dlg.Draw(scr, theme, loc);
        Assert.Contains(scr.ComputeDiff(), o =>
            o.Fg.Equals(theme.ButtonSelFg) && o.Bg.Equals(theme.ButtonSelBg));
    }

    [Fact]
    public void HoverNullFallsBackToSelection()
    {
        var loc = En();
        var theme = Themes.Get("dark");
        var scr = new Screen();
        scr.Resize(W, H);
        var dlg = new ModalDialog(ModalState.Overwrite(loc, "a.txt"), (_, _) => { });
        dlg.HoverActive = true;
        dlg.HoverButton = null;
        dlg.Draw(scr, theme, loc);
        // Выбор (кнопка 0) виден — свежеоткрытая модалка не слепая.
        Assert.Contains(scr.ComputeDiff(), o =>
            o.Fg.Equals(theme.ButtonSelFg) && o.Bg.Equals(theme.ButtonSelBg));
    }

    [Fact]
    public void PressClosesDialog()
    {
        var loc = En();
        ModalKeyOutcome? seen = null;
        var dlg = new ModalDialog(ModalState.Overwrite(loc, "a.txt"),
            (_, o) => { seen = o; });
        // Ищем первую клетку-кнопку сканом (true бывает и мимо кнопок — глушение) и жмём.
        int fx = -1, fy = -1;
        for (int y = 0; y < H && fx < 0; y++)
            for (int x = 0; x < W && fx < 0; x++)
            {
                int cx = x, cy = y;
                var probe = new ModalDialog(ModalState.Overwrite(loc, "a.txt"),
                    (_, _) => { fx = cx; fy = cy; });
                probe.HandleClick(x, y, W, H, loc);
            }
        Assert.True(fx >= 0);
        dlg.HandleClick(fx, fy, W, H, loc);
        Assert.True(dlg.Closed);
        Assert.NotNull(seen);
        Assert.False(seen.Value.Cancelled);
    }
}
