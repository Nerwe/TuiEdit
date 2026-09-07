using System.Text.Json;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>
/// Паритет strings.ru/en.json и наличие всех ключей, используемых кодом и справкой.
/// Сводка проверок бывших сьютов: падает при ключе только в одном языке.
/// </summary>
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
    [InlineData("msg.restored")]
    [InlineData("settings.useregex")]
    [InlineData("msg.search.badpattern")]
    [InlineData("help.k1")]
    [InlineData("help.k12")]
    [InlineData("help.k13")]
    [InlineData("help.k14")]
    [InlineData("help.k15")]
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
    public void KeyExistsInBoth(string key)
    {
        var (ru, en) = Both();
        Assert.True(ru.ContainsKey(key), "ru missing " + key);
        Assert.True(en.ContainsKey(key), "en missing " + key);
    }

    [Fact]
    public void RemovedKeysStayRemoved()
    {
        var (ru, en) = Both();
        Assert.DoesNotContain("help.note1", ru.Keys);
        Assert.DoesNotContain("help.note1", en.Keys);
    }

    [Fact]
    public void AboutHasNoKeyHints()
    {
        // Окно «О программе» — только информация: без хоткеев и подсказок закрытия.
        var (ru, en) = Both();
        foreach (var (lang, map) in new[] { ("ru", ru), ("en", en) })
            foreach (string key in map.Keys.Where(k => k.StartsWith("modal.about.")))
                Assert.DoesNotContain("Esc", map[key], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modal.about.l3", ru.Keys);
        Assert.DoesNotContain("modal.about.l3", en.Keys);
    }

    [Fact]
    public void HelpK1DocumentsSave()
    {
        var (ru, _) = Both();
        Assert.Contains("^S", ru["help.k1"]);
        Assert.DoesNotContain("F2", ru["help.k1"]);
    }
}
