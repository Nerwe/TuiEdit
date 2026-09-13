using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Frame-perf regression locks: batched ANSI output and depth-independent viewport.</summary>
public sealed class FramePerfTests
{
    [Fact]
    public void BuildAnsiFrameAddressesInlineAndDedupsColors()
    {
        var fg = new Rgb(1, 2, 3);
        var bg = new Rgb(4, 5, 6);
        var ops = new List<Screen.DrawOp>
        {
            new(0, 0, "hi", fg, bg),
            new(5, 2, "yo", fg, bg),
        };
        Assert.Equal("\x1b[1;1H\x1b[38;2;1;2;3m\x1b[48;2;4;5;6mhi\x1b[3;6Hyo\x1b[0m",
            Screen.BuildAnsiFrame(ops));
    }

    [Fact]
    public void BuildAnsiFrameReemitsColorOnChange()
    {
        var fg = new Rgb(1, 2, 3);
        var bg = new Rgb(4, 5, 6);
        var fg2 = new Rgb(7, 8, 9);
        var ops = new List<Screen.DrawOp>
        {
            new(0, 0, "a", fg, bg),
            new(1, 0, "b", fg2, bg),
        };
        Assert.Equal("\x1b[1;1H\x1b[38;2;1;2;3m\x1b[48;2;4;5;6ma\x1b[1;2H\x1b[38;2;7;8;9mb\x1b[0m",
            Screen.BuildAnsiFrame(ops));
    }

    [Fact]
    public void DeepCursorKeepsCursorLineVisible()
    {
        var lines = new List<string>();
        for (int i = 0; i < 5000; i++)
            lines.Add($"line {i}");
        var buf = new TextBuffer(null);
        buf.RestoreContent(lines);
        var ed = new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.GoToLineNumber(5000);
        ed.RenderFrame(80, 24);
        Screen screen = GetScreen(ed);
        Assert.Contains(screen.Snapshot(), row => row.Contains("line 4999"));
    }

    private static Screen GetScreen(TuiEditor ed) =>
        (Screen)typeof(TuiEditor).GetField("_screen",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(ed)!;
}
