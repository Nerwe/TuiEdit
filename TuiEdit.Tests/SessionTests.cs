using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

[Trait("Category", "Integration")]
public sealed class SessionTests : IDisposable
{
    private readonly string _dir;

    public SessionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_session_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private TuiEditor NewEditor(AppSettings settings) =>
        new(new TextBuffer(null), settings,
            new SettingsStore(Path.Combine(_dir, "settings.json")));

    private string WriteFile(string name, params string[] lines)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllLines(p, lines);
        return p;
    }

    [Fact]
    public void SaveAllSavesNamedSkipsUnnamed()
    {
        string f1 = WriteFile("one.txt", "one");
        var buf1 = new TextBuffer(f1);
        buf1.InsertChar(0, 0, 'X');
        var settings = new AppSettings();
        var ed = new TuiEditor(buf1, settings,
            new SettingsStore(Path.Combine(_dir, "settings.json")));
        ed.NewTab();
        var active = (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        active.InsertChar(0, 0, 'Y');
        ed.SaveAll();
        Assert.StartsWith("Xone", File.ReadAllText(f1));
        Assert.False(buf1.IsModified);
        Assert.True(active.IsModified);
        Assert.Equal(2, ed.TabCount);
    }

    [Fact]
    public void RestoreOpensFilesAndSkipsMissing()
    {
        string p1 = WriteFile("a.txt", "one", "two", "three");
        string p2 = WriteFile("b.txt", "x");
        var s = new AppSettings { RestoreSession = true };
        s.SessionTabs.Add(new SessionTab(Path.Combine(_dir, "gone.txt"), 0, 0));
        s.SessionTabs.Add(new SessionTab(p1, 5, 99));
        s.SessionTabs.Add(new SessionTab(p2, 0, 0));
        var ed = NewEditor(s);
        Assert.Equal(2, ed.RestoreSessionTabs());
        Assert.Equal(2, ed.TabCount);
    }

    [Fact]
    public void RestoreDisabledAddsNothing()
    {
        string p1 = WriteFile("a.txt", "one");
        var s = new AppSettings { RestoreSession = false };
        s.SessionTabs.Add(new SessionTab(p1, 0, 0));
        var ed = NewEditor(s);
        Assert.Equal(0, ed.RestoreSessionTabs());
        Assert.Equal(1, ed.TabCount);
    }

    [Fact]
    public void UntitledRoundTrip()
    {
        var s = new AppSettings { RestoreSession = true };
        var ed = NewEditor(s);
        var buf = (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        buf.InsertChar(0, 0, 'X');
        ed.SaveSessionTabs();
        Assert.Single(s.SessionTabs);
        Assert.Equal(string.Empty, s.SessionTabs[0].Path);
        Assert.Equal(new List<string> { "X" }, s.SessionTabs[0].Lines);

        var s2 = new AppSettings { RestoreSession = true, SessionTabs = s.SessionTabs };
        var ed2 = NewEditor(s2);
        Assert.Equal(1, ed2.RestoreSessionTabs());
        var buf2 = (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed2)!;
        Assert.Equal("X", buf2.GetLine(0));
        Assert.True(buf2.IsModified);
        Assert.Null(buf2.FilePath);
    }

    [Fact]
    public void UntitledOversizedSkipped()
    {
        var s = new AppSettings { RestoreSession = true };
        var ed = NewEditor(s);
        var buf = (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        buf.InsertString(0, 0, new string('z', AppSettings.MaxUntitledSessionChars + 1));
        ed.SaveSessionTabs();
        Assert.Empty(s.SessionTabs);
    }

    [Fact]
    public void CorruptSettingsBackedUp()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{not json");
        var store = new SettingsStore(path);
        AppSettings s = store.Load();
        Assert.Equal("dark", s.Theme); // defaults
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.Equal("{not json", File.ReadAllText(path + ".corrupt"));
    }

    [Fact]
    public void NormalizeNullLists()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"Theme":"nope","Themes":null,"SessionTabs":null,"RecentFiles":null}""");
        AppSettings s = new SettingsStore(path).Load();
        Assert.Equal("dark", s.Theme);
        Assert.NotNull(s.Themes);
        Assert.NotNull(s.SessionTabs);
        Assert.NotNull(s.RecentFiles);
    }

    [Fact]
    public void AtomicSaveLeavesNoTmp()
    {
        string path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Theme = "light" });
        store.Save(new AppSettings { Theme = "dark" });
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal("dark", store.Load().Theme);
    }

    [Fact]
    public void SaveRoundtripThroughStore()
    {
        string p1 = WriteFile("a.txt", "one", "two", "three");
        var s = new AppSettings { RestoreSession = true };
        s.SessionTabs.Add(new SessionTab(p1, 99, 99));
        var ed = NewEditor(s);
        ed.RestoreSessionTabs();
        ed.SaveSessionTabs();
        Assert.Single(s.SessionTabs);
        Assert.Equal(p1, s.SessionTabs[0].Path);
        Assert.Equal(3, s.SessionTabs[0].Row); // 3 lines + empty tail from the trailing newline
        Assert.Contains("SessionTabs", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        var s2 = new AppSettings { RestoreSession = true, SessionTabs = s.SessionTabs };
        var ed2 = NewEditor(s2);
        Assert.Equal(1, ed2.RestoreSessionTabs());
    }
}
