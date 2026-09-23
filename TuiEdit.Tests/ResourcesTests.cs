using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Parity of strings.ru/en.json and presence of all keys used by code and help.</summary>
public sealed class ResourcesTests
{
    private static Dictionary<string, string> Load(string lang)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", $"strings.{lang}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static (Dictionary<string, string> ru, Dictionary<string, string> en) Both()
    {
        var ru = Load("ru");
        var en = Load("en");
        Assert.Equal(ru.Count, en.Count);
        Assert.Empty(ru.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ru.Keys));
        return (ru, en);
    }

    [Fact]
    public void Parity()
    {
        Both();
    }

    [Fact]
    public void DefaultLanguageIsEnglish()
    {
        Assert.Equal("en", new AppSettings().Language);
        Assert.Equal("en", Loc.Normalize(null));
        Assert.Equal("en", Loc.Normalize(""));
        Assert.Equal("en", Loc.Normalize("de"));
        Assert.Equal("ru", Loc.Normalize("ru"));
        Assert.Equal("en", Loc.Supported[0]);
    }

    [Theory]
    [InlineData("menu.helpitem")]
    [InlineData("settings.shownumbers")]
    [InlineData("settings.wordwrap")]
    [InlineData("help.k11")]
    [InlineData("menu.recent")]
    [InlineData("modal.recent.title")]
    [InlineData("msg.recent.empty")]
    [InlineData("settings.backup")]
    [InlineData("modal.restore.title")]
    [InlineData("modal.restore.desc")]
    [InlineData("modal.unsaved.descfile")]
    [InlineData("msg.restored")]
    [InlineData("settings.useregex")]
    [InlineData("msg.search.badpattern")]
    [InlineData("help.k1")]
    [InlineData("help.k12")]
    [InlineData("help.k13")]
    [InlineData("help.k14")]
    [InlineData("help.k15")]
    [InlineData("help.k16")]
    [InlineData("help.k17")]
    [InlineData("help.k18")]
    [InlineData("help.k19")]
    [InlineData("help.k20")]
    [InlineData("picker.mkdir.empty")]
    [InlineData("picker.mkdir.exists")]
    [InlineData("modal.delete.confirm")]
    [InlineData("modal.delete.one")]
    [InlineData("modal.delete.many")]
    [InlineData("menu.saveall")]
    [InlineData("msg.savedall")]
    [InlineData("msg.hint")]
    [InlineData("menu.format")]
    [InlineData("format.title")]
    [InlineData("format.encoding")]
    [InlineData("format.endings")]
    [InlineData("format.indent")]
    [InlineData("menu.sortlines")]
    [InlineData("msg.sorted")]
    [InlineData("msg.stats")]
    [InlineData("settings.whitespace")]
    [InlineData("settings.ruler")]
    [InlineData("menu.grep")]
    [InlineData("prompt.grep.pattern")]
    [InlineData("prompt.grep.dir")]
    [InlineData("modal.grep.title")]
    [InlineData("msg.bookmark.set")]
    [InlineData("msg.bookmark.cleared")]
    [InlineData("msg.bookmark.none")]
    [InlineData("msg.fold.closed")]
    [InlineData("msg.fold.opened")]
    [InlineData("msg.fold.none")]
    [InlineData("msg.complete.none")]
    [InlineData("modal.complete.title")]
    [InlineData("status.readonly")]
    [InlineData("error.readonly")]
    [InlineData("modal.replace.title")]
    [InlineData("modal.replace.desc")]
    [InlineData("modal.tabs.title")]
    [InlineData("menu.newtab")]
    [InlineData("menu.closetab")]
    [InlineData("modal.about.title")]
    [InlineData("modal.about.ok")]
    [InlineData("help.dlg.title")]
    [InlineData("help.dlg.hint")]
    [InlineData("help.sec.file")]
    [InlineData("help.sec.edit")]
    [InlineData("help.sec.find")]
    [InlineData("help.sec.nav")]
    [InlineData("help.sec.menu")]
    [InlineData("help.sec.view")]
    [InlineData("help.sec.manager")]
    [InlineData("modal.about.date")]
    [InlineData("modal.about.author")]
    [InlineData("modal.about.license")]
    [InlineData("dashboard.title")]
    [InlineData("dashboard.version")]
    [InlineData("dashboard.recent")]
    [InlineData("dashboard.empty")]
    [InlineData("dashboard.hint")]
    public void KeyExistsInBoth(string key)
    {
        var (ru, en) = Both();
        Assert.True(ru.ContainsKey(key), "ru missing " + key);
        Assert.True(en.ContainsKey(key), "en missing " + key);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void FormatPlaceholdersSatisfiable(string lang)
    {
        foreach ((string key, string value) in Load(lang))
        {
            foreach (Match m in Regex.Matches(value, @"\{(\d+)"))
            {
                int index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.True(index <= 2,
                    $"{lang}:{key} references {{{index}}}, call sites pass at most 3 args");
            }
            // Three dummies must substitute everything (typos would ship as "{0}" text).
            string formatted = string.Format(CultureInfo.InvariantCulture, value, "A", "B", "C");
            Assert.DoesNotContain("{", formatted);
            Assert.DoesNotContain("}", formatted);
        }
    }

    [Fact]
    public void AboutHasNoKeyHints()
    {
        // The "About" window is info-only: no hotkeys or close hints.
        var (ru, en) = Both();
        foreach (var (lang, map) in new[] { ("ru", ru), ("en", en) })
            foreach (string key in map.Keys.Where(k => k.StartsWith("modal.about.", StringComparison.Ordinal)))
                Assert.DoesNotContain("Esc", map[key], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modal.about.l3", ru.Keys);
        Assert.DoesNotContain("modal.about.l3", en.Keys);
    }

    [Fact]
    public void HelpK1DocumentsSave()
    {
        var (ru, _) = Both();
        Assert.Contains("^S", ru["help.k1"]);
    }
}
