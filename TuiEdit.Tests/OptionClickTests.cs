using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Clicks on option rows (settings/format) and prompt toggles.</summary>
[Trait("Category", "Integration")]
public sealed class OptionClickTests
{
    private const int W = 80;
    private const int H = 24;

    private static Loc En() => Loc.Load("en");

    private static SettingsStore TmpStore(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "tui_opt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsStore(Path.Combine(dir, "s.json"));
    }

    /// <summary>First clickable cell of each row, top to bottom.</summary>
    private static List<(int X, int Y)> ClickCells(Func<int, int, bool> click)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (click(x, y))
                {
                    cells.Add((x, y));
                    break;
                }
        return cells;
    }

    [Fact]
    public void SettingsClickSelectsAndSteps()
    {
        var loc = En();
        var settings = new AppSettings();
        var store = TmpStore(out string dir);
        try
        {
            bool changed = false;
            var dlg = new SettingsDialog(settings, store, () => { changed = true; });
            bool before = settings.ShowLineNumbers; // row 2
            List<(int X, int Y)> rows = ClickCells((x, y) =>
                new SettingsDialog(new AppSettings(), store, () => { }).HandleClick(x, y, W, H, loc));
            // Header + 13 rows + bottom: everything inside the box is swallowed, the middle 13 are options.
            Assert.Equal(15, rows.Count);
            dlg.HandleClick(rows[3].X, rows[3].Y, W, H, loc); // 3rd row from the top = index 2
            Assert.Equal(!before, settings.ShowLineNumbers);
            Assert.True(changed);
            Assert.False(dlg.Closed); // options do not close the dialog
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void SettingsClickCyclesMouseLevels()
    {
        var loc = En();
        var settings = new AppSettings();
        Assert.Equal(MouseLevel.Off, settings.Mouse);
        var store = TmpStore(out string dir);
        try
        {
            bool changed = false;
            var dlg = new SettingsDialog(settings, store, () => { changed = true; });
            List<(int X, int Y)> rows = ClickCells((x, y) =>
                new SettingsDialog(new AppSettings(), store, () => { }).HandleClick(x, y, W, H, loc));
            Assert.Equal(15, rows.Count);
            (int X, int Y) mouse = rows[^5]; // option row = index 9 (mouse)
            MouseLevel[] expected = [MouseLevel.Basic, MouseLevel.Drag, MouseLevel.Motion, MouseLevel.Off];
            foreach (MouseLevel level in expected)
            {
                dlg.HandleClick(mouse.X, mouse.Y, W, H, loc);
                Assert.Equal(level, settings.Mouse);
            }
            (int X, int Y) copy = rows[^4]; // option row = index 10 (copy)
            Assert.True(settings.CopyOnSelect);
            dlg.HandleClick(copy.X, copy.Y, W, H, loc);
            Assert.False(settings.CopyOnSelect);
            Assert.True(changed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void MouseDisabledByDefault()
    {
        Assert.Equal(MouseLevel.Off, new AppSettings().Mouse);
        Assert.False(InputReader.MouseEnabled);
    }

    [Fact]
    public void MouseSequencesPerLevel()
    {
        Assert.Equal("", Terminal.MouseEnableSequence(MouseLevel.Off));
        Assert.Equal("\x1b[?1002l\x1b[?1003l\x1b[?1000h\x1b[?1006h",
            Terminal.MouseEnableSequence(MouseLevel.Basic));
        Assert.Equal("\x1b[?1003l\x1b[?1000h\x1b[?1002h\x1b[?1006h",
            Terminal.MouseEnableSequence(MouseLevel.Drag));
        Assert.Equal("\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h",
            Terminal.MouseEnableSequence(MouseLevel.Motion));
        Assert.Equal("\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l",
            Terminal.MouseDisableSequence());
    }

    [Fact]
    public void MouseMigratesFromLegacyFlag()
    {
        var s = new AppSettings { EnableMouse = true };
        s.Normalize();
        Assert.Equal(MouseLevel.Basic, s.Mouse);
        Assert.False(s.EnableMouse);
    }

    [Fact]
    public void ConsoleQueueApiNeverThrows()
    {
        // In CI stdin is redirected: graceful degradation, no exceptions.
        // Locally at the console — also safe (only peek/wait(0), no Take).
        var ex = Record.Exception(() =>
        {
            Terminal.TryPeek(out _);
            Terminal.WaitForInput(0);
            Terminal.IsKeyPending();
            Terminal.PendingCount();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void SettingsClickOutsideIgnored()
    {
        var loc = En();
        var settings = new AppSettings();
        var store = TmpStore(out string dir);
        try
        {
            var dlg = new SettingsDialog(settings, store, () => { });
            Assert.False(dlg.HandleClick(0, 0, W, H, loc));
            Assert.False(dlg.Closed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void FormatClickCyclesEnding()
    {
        var loc = En();
        var buf = new TextBuffer(null);
        var dlg = new FormatDialog(buf);
        string before = buf.EndingLabel;
        List<(int X, int Y)> rows = ClickCells((x, y) =>
            new FormatDialog(new TextBuffer(null)).HandleClick(x, y, W, H, loc));
        Assert.Equal(5, rows.Count); // header + 3 rows + bottom
        dlg.HandleClick(rows[2].X, rows[2].Y, W, H, loc); // 2nd row from the top = index 1 (line endings)
        Assert.NotEqual(before, buf.EndingLabel);
        Assert.False(dlg.Closed);
    }

    [Theory]
    [InlineData(0, 'C')]
    [InlineData(21, 'C')]
    [InlineData(25, 'W')]
    [InlineData(50, 'R')]
    public void PromptOptionHitMapsSegments(int x, char want)
    {
        // Mirror of DrawPrompt: "[x] Match case (Alt+C)  [ ] Whole words (Alt+W)  [ ] Regex (Alt+R)".
        char? got = TuiEditor.PromptOptionHit(x, "Match case", "Whole words", "Regex");
        Assert.Equal(want, got);
    }

    [Theory]
    [InlineData(22)] // space separator
    [InlineData(200)]
    public void PromptOptionHitMiss(int x)
    {
        Assert.Null(TuiEditor.PromptOptionHit(x, "Match case", "Whole words", "Regex"));
    }
}
