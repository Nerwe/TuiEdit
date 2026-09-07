using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Инкрементальный поиск: живой термин важнее последнего (курсор не двигаем).</summary>
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
        Assert.Equal("", ed.EffectiveSearchTerm); // пустой ввод — без подсветки
        ed._liveSearch = null;
        Assert.Equal("foo", ed.EffectiveSearchTerm);
    }

    [Fact]
    public void PartialTermSafe()
    {
        // Недописанный regex/пустой ввод не должны ронять маску подсветки.
        var m = typeof(TuiEditor).GetMethod("FindMatches", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool[] Mask(string text, string term, bool rx) =>
            (bool[])m.Invoke(null, [text, term, true, false, rx])!;
        Assert.All(Mask("foo (bar", "", false), b => Assert.False(b));
        Assert.All(Mask("foo (bar", "(", true), b => Assert.False(b));
        Assert.Contains(Mask("foo bar foo", "foo", false), b => b);
    }
}
