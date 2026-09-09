using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Incremental search: the live term beats the last one (we do not move the cursor).</summary>
[Trait("Category", "Integration")]
public sealed class LiveSearchTests : IDisposable
{
    private readonly string _cfgDir;

    public LiveSearchTests()
    {
        _cfgDir = Path.Combine(Path.GetTempPath(), "tui_live_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cfgDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cfgDir, true); } catch { }
    }

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(), new SettingsStore(Path.Combine("x", "settings.json")));
    }

    private static void SetLast(TuiEditor ed, string term) =>
        typeof(TuiEditor).GetField("_lastSearch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(ed, term);

    [Fact]
    public void LiveOverridesLast()
    {
        var ed = NewEditor();
        Assert.Equal("", ed.EffectiveSearchTerm);
        SetLast(ed, "foo");
        Assert.Equal("foo", ed.EffectiveSearchTerm);
        ed._liveSearch = "f";
        Assert.Equal("f", ed.EffectiveSearchTerm);
        ed._liveSearch = "";
        Assert.Equal("", ed.EffectiveSearchTerm); // empty input — no highlight
        ed._liveSearch = null;
        Assert.Equal("foo", ed.EffectiveSearchTerm);
    }

    [Fact]
    public void SearchOptionsToggle()
    {
        var settings = new AppSettings();
        var ed = new TuiEditor(new TextBuffer(null), settings,
            new SettingsStore(Path.Combine(_cfgDir, "settings.json")));
        Assert.True(ed.ToggleSearchOption(ConsoleKey.C));
        Assert.False(settings.SearchMatchCase);
        Assert.True(ed.ToggleSearchOption(ConsoleKey.W));
        Assert.True(settings.SearchWholeWord);
        Assert.True(ed.ToggleSearchOption(ConsoleKey.R));
        Assert.True(settings.SearchUseRegex);
        Assert.False(ed.ToggleSearchOption(ConsoleKey.X));
        Assert.True(File.Exists(Path.Combine(_cfgDir, "settings.json")));
    }

    [Fact]
    public void PromptOptionsRowRenders()
    {
        var settings = new AppSettings();
        var ed = new TuiEditor(new TextBuffer(null), settings,
            new SettingsStore(Path.Combine(_cfgDir, "settings.json")));
        var scr = (Screen)typeof(TuiEditor).GetField("_screen",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        scr.Resize(96, 28);
        typeof(TuiEditor).GetMethod("DrawPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, ["Find: ", "te", 2, true]);
        var cur = (Array)typeof(Screen).GetField("_cur",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scr)!;
        string RowText(int y)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < scr.Width; x++)
                sb.Append((char)cur.GetValue(x, y)!.GetType().GetProperty("Ch")!.GetValue(cur.GetValue(x, y))!);
            return sb.ToString();
        }
        string opts = RowText(26);
        Assert.Contains("[x] Match case (Alt+C)", opts);
        Assert.Contains("[ ] Whole words (Alt+W)", opts);
        Assert.Contains("[ ] Regex (Alt+R)", opts);
        Assert.StartsWith("Find: te", RowText(27));
    }

    [Fact]
    public void PartialTermSafe()
    {
        // An unfinished regex/empty input must not drop the highlight mask.
        var m = typeof(TuiEditor).GetMethod("FindMatches", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool[] Mask(string text, string term, bool rx) =>
            (bool[])m.Invoke(null, [text, term, true, false, rx])!;
        Assert.All(Mask("foo (bar", "", false), b => Assert.False(b));
        Assert.All(Mask("foo (bar", "(", true), b => Assert.False(b));
        Assert.Contains(Mask("foo bar foo", "foo", false), b => b);
    }
}
