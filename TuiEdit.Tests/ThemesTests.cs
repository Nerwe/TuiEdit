using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Пользовательские темы из конфига: hex, каталог, резолв, диалог.</summary>
public sealed class ThemesTests : IDisposable
{
    private readonly string _cfgDir;

    public ThemesTests()
    {
        _cfgDir = Path.Combine(Path.GetTempPath(), "tui_th_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cfgDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cfgDir, true); } catch { }
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    private static AppSettings SettingsWith(params ThemeScheme[] schemes)
    {
        var s = new AppSettings();
        s.Themes.AddRange(schemes);
        s.Normalize();
        return s;
    }

    [Theory]
    [InlineData("#090300", 0x09, 0x03, 0x00)]
    [InlineData("090300", 0x09, 0x03, 0x00)]
    [InlineData("#ABCDEF", 0xAB, 0xCD, 0xEF)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    [InlineData("fff", 0xFF, 0xFF, 0xFF)]
    public void HexValid(string s, byte r, byte g, byte b)
    {
        Assert.True(ThemeScheme.TryParseHex(s, out Rgb rgb));
        Assert.Equal(new Rgb(r, g, b), rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#zzzzzz")]
    [InlineData("red")]
    public void HexInvalid(string? s)
    {
        Assert.False(ThemeScheme.TryParseHex(s, out _));
    }

    [Fact]
    public void ResolveFallback()
    {
        var s = new AppSettings();
        Assert.Equal(Themes.Dark, ThemeCatalog.Resolve(s, "nope"));
        Assert.Equal(Themes.Dark, ThemeCatalog.Resolve(s, null));
        Assert.Equal(Themes.Dark, ThemeCatalog.Resolve(s, "dark"));
        Assert.Equal(Themes.Light, ThemeCatalog.Resolve(s, "light"));
    }

    [Fact]
    public void ResolveCustomOverBase()
    {
        var s = SettingsWith(new ThemeScheme
        {
            Name = "3024 Night",
            Base = "dark",
            Colors = new Dictionary<string, string>
            {
                ["EditorBg"] = "#090300",
                ["editorfg"] = "#a5a2a2", // регистр ролей не важен
                ["Nope"] = "#ffffff", // неизвестная роль — мимо
                ["StatusBg"] = "oops", // битый цвет — мимо
            },
        });
        Theme t = ThemeCatalog.Resolve(s, "3024 night"); // имя — case-insensitive
        Assert.Equal("3024 Night", t.Name);
        Assert.Equal(new Rgb(0x09, 0x03, 0x00), t.EditorBg);
        Assert.Equal(new Rgb(0xA5, 0xA2, 0xA2), t.EditorFg);
        Assert.Equal(Themes.Dark.StatusBg, t.StatusBg); // битый — из базы
        Assert.Equal(Themes.Dark.MatchBg, t.MatchBg); // незаданный — из базы
    }

    [Fact]
    public void CustomOverridesBuiltin()
    {
        var s = SettingsWith(new ThemeScheme
        {
            Name = "dark",
            Colors = new Dictionary<string, string> { ["EditorBg"] = "#000000" },
        });
        Assert.Equal(new Rgb(0, 0, 0), ThemeCatalog.Resolve(s, "dark").EditorBg);
        Assert.Equal(new List<string> { "dark", "light", "3024 Night (dark)", "Paper (light)" },
            ThemeCatalog.Names(s)); // без дубля
    }

    [Fact]
    public void BaseLight()
    {
        var s = SettingsWith(new ThemeScheme { Name = "Paper", Base = "light" });
        Theme t = ThemeCatalog.Resolve(s, "Paper");
        Assert.Equal(Themes.Light.EditorBg, t.EditorBg);
    }

    [Fact]
    public void BuiltinExtras()
    {
        var s = new AppSettings();
        Theme night = ThemeCatalog.Resolve(s, "3024 Night (dark)");
        Assert.Equal("3024 Night (dark)", night.Name);
        Assert.Equal(new Rgb(0x09, 0x03, 0x00), night.EditorBg);
        Assert.Equal(new Rgb(0xA5, 0xA2, 0xA2), night.EditorFg);
        Theme paper = ThemeCatalog.Resolve(s, "Paper (light)");
        Assert.Equal("Paper (light)", paper.Name);
        Assert.Equal(new Rgb(0xF7, 0xF3, 0xEA), paper.EditorBg);
        // Имена со скобками показываются как есть.
        var loc = Loc.Load("en");
        Assert.Equal("3024 Night (dark)", ThemeCatalog.DisplayName(loc, "3024 Night (dark)"));
        Assert.Equal("Paper (light)", ThemeCatalog.DisplayName(loc, "Paper (light)"));
        // Своя схема может перекрыть и новую встроенную.
        s.Themes.Add(new ThemeScheme
        {
            Name = "Paper (light)",
            Colors = new Dictionary<string, string> { ["EditorBg"] = "#ffffff" },
        });
        Assert.Equal(new Rgb(0xFF, 0xFF, 0xFF), ThemeCatalog.Resolve(s, "Paper (light)").EditorBg);
    }

    [Fact]
    public void IndentGuideRole()
    {
        var s = new AppSettings();
        Assert.True(s.ShowIndentGuides);
        Assert.Equal(Themes.Dark.IndentGuideFg, ThemeCatalog.Resolve(s, "dark").IndentGuideFg);
        s.Themes.Add(new ThemeScheme
        {
            Name = "Mine",
            Colors = new Dictionary<string, string> { ["IndentGuideFg"] = "#112233" },
        });
        Assert.Equal(new Rgb(0x11, 0x22, 0x33), ThemeCatalog.Resolve(s, "Mine").IndentGuideFg);
    }

    [Fact]
    public void NamesAndDisplay()
    {
        var s = SettingsWith(new ThemeScheme { Name = "Mine" }, new ThemeScheme { Name = " " });
        Assert.Equal(new List<string> { "dark", "light", "3024 Night (dark)", "Paper (light)", "Mine" },
            ThemeCatalog.Names(s));
        var loc = Loc.Load("en");
        Assert.Equal("Dark", ThemeCatalog.DisplayName(loc, "dark"));
        Assert.Equal("Light", ThemeCatalog.DisplayName(loc, "light"));
        Assert.Equal("Mine", ThemeCatalog.DisplayName(loc, "Mine"));
        Assert.True(ThemeCatalog.Contains(s, "mine"));
        Assert.False(ThemeCatalog.Contains(s, "nope"));
    }

    [Fact]
    public void NormalizeKeepsCustom()
    {
        var s = new AppSettings { Theme = "Mine" };
        s.Themes.Add(new ThemeScheme { Name = "Mine" });
        s.Themes.Add(new ThemeScheme { Name = "  " });
        s.Normalize();
        Assert.Equal("Mine", s.Theme);
        Assert.Single(s.Themes);
        s.Theme = "nope";
        s.Normalize();
        Assert.Equal("dark", s.Theme);
    }

    [Fact]
    public void StoreRoundtripTolerant()
    {
        var store = new SettingsStore(Path.Combine(_cfgDir, "settings.json"));
        var s = new AppSettings { Theme = "Mine" };
        s.Themes.Add(new ThemeScheme
        {
            Name = "Mine",
            Colors = new Dictionary<string, string> { ["EditorBg"] = "#111111" },
        });
        store.Save(s);
        // Ручные правки: комментарии, запятая, camelCase.
        string json = File.ReadAllText(store.Path)
            .Replace("\"Theme\"", "// тема\n\"theme\"")
            .Replace("}\n}", "},\n}");
        File.WriteAllText(store.Path, json);
        AppSettings back = store.Load();
        Assert.Equal("Mine", back.Theme);
        Assert.Equal("#111111", back.Themes[0].Colors["EditorBg"]);
        Assert.Equal(new Rgb(0x11, 0x11, 0x11),
            ThemeCatalog.Resolve(back, "Mine").EditorBg);
    }

    [Fact]
    public void DialogCyclesCustom()
    {
        var settings = SettingsWith(new ThemeScheme { Name = "Mine" });
        var store = new SettingsStore(Path.Combine(_cfgDir, "settings.json"));
        bool applied = false;
        var dlg = new SettingsDialog(settings, store, () => { applied = true; });
        dlg.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal("light", settings.Theme);
        dlg.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal("3024 Night (dark)", settings.Theme);
        dlg.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal("Paper (light)", settings.Theme);
        dlg.HandleKey(K('\0', ConsoleKey.RightArrow));
        Assert.Equal("Mine", settings.Theme);
        Assert.True(applied);
        Assert.True(File.Exists(Path.Combine(_cfgDir, "settings.json")));
        dlg.HandleKey(K('\0', ConsoleKey.LeftArrow));
        Assert.Equal("Paper (light)", settings.Theme);
    }
}
