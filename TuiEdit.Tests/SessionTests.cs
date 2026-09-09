using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

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
