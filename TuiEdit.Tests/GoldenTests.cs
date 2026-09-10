using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Golden frames for dialogs: exact screen snapshots. Regenerate with
// UPDATE_GOLDENS=1 and review the diff before committing.
public sealed class GoldenTests
{
    private readonly Theme _theme = Themes.Get("dark");
    private readonly Loc _loc = Loc.Load("en");

    private static Screen NewScreen(int w = 80, int h = 24)
    {
        var scr = new Screen();
        scr.Resize(w, h);
        return scr;
    }

    [Fact]
    public void HelpDialogFrame()
    {
        var scr = NewScreen();
        new HelpDialog(_loc).Draw(scr, _theme, _loc);
        Golden.AssertMatch("help.en.txt", scr);
    }

    [Fact]
    public void SettingsDialogFrame()
    {
        var scr = NewScreen();
        var store = new SettingsStore(Path.Combine("x", "settings.json"));
        new SettingsDialog(new AppSettings(), store, () => { }).Draw(scr, _theme, _loc);
        Golden.AssertMatch("settings.en.txt", scr);
    }

    [Fact]
    public void AboutModalFrame()
    {
        var scr = NewScreen();
        new ModalDialog(ModalState.About(_loc, "0.5.0"), (_, _) => { }).Draw(scr, _theme, _loc);
        Golden.AssertMatch("about.en.txt", scr);
    }

    [Fact]
    public void ErrorModalFrame()
    {
        var scr = NewScreen();
        var dlg = new ModalDialog(
            ModalState.Error(_loc, "Open error", "Access to the path is denied even though it looked readable."),
            (_, _) => { });
        dlg.Draw(scr, _theme, _loc);
        Golden.AssertMatch("error.en.txt", scr);
    }

    [Fact]
    public void PaletteFilteredFrame()
    {
        var scr = NewScreen();
        var store = new SettingsStore(Path.Combine("x", "settings.json"));
        var dlg = new CommandPaletteDialog(new AppSettings(), store, () => { }, _ => { });
        dlg.HandleKey(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        dlg.HandleKey(new ConsoleKeyInfo('v', ConsoleKey.V, false, false, false));
        dlg.Draw(scr, _theme, _loc);
        Golden.AssertMatch("palette-sav.en.txt", scr);
    }

    [Fact]
    public void GrepModalFrame()
    {
        var scr = NewScreen();
        var hits = new List<GrepHit>
        {
            new("src/long/path/to/file.cs", 12, 4, "var needle = FindMe();"),
            new("src/other.cs", 3, 0, "// needle in a comment"),
        };
        new ModalDialog(ModalState.Grep(_loc, hits), (_, _) => { }).Draw(scr, _theme, _loc);
        Golden.AssertMatch("grep.en.txt", scr);
    }
}
