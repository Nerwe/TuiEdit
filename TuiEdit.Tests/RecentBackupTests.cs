using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Недавние файлы, .bak и попап Recent.</summary>
public sealed class RecentBackupTests
{
    private static ConsoleKeyInfo Arrow(ConsoleKey k) => new('\0', k, false, false, false);

    [Fact]
    public void TouchRecentOrderDedupCap()
    {
        var s = new AppSettings();
        s.TouchRecent("b.txt");
        s.TouchRecent("a.txt");
        s.TouchRecent("b.txt");
        Assert.Equal(2, s.RecentFiles.Count);
        Assert.EndsWith("b.txt", s.RecentFiles[0]);
        Assert.EndsWith("a.txt", s.RecentFiles[1]);
        s.TouchRecent("");
        s.TouchRecent("   ");
        Assert.Equal(2, s.RecentFiles.Count);
        for (int i = 0; i < 25; i++) s.TouchRecent($"f{i}.txt");
        Assert.Equal(AppSettings.MaxRecentFiles, s.RecentFiles.Count);
        Assert.EndsWith("f24.txt", s.RecentFiles[0]);
        Assert.True(Path.IsPathFullyQualified(s.RecentFiles[0]));
    }

    [Fact]
    public void PruneRecentDropsMissing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_mru_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string real = Path.Combine(dir, "real.txt");
            File.WriteAllText(real, "x");
            var s = new AppSettings();
            s.TouchRecent(real);
            s.TouchRecent(Path.Combine(dir, "ghost.txt"));
            Assert.Equal(2, s.RecentFiles.Count);
            s.PruneRecent();
            Assert.Single(s.RecentFiles);
            Assert.Equal(real, s.RecentFiles[0]);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void SaveBackupGoesToCentralStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_bak_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string bakDir = Path.Combine(dir, "backups");
            var store = new BackupStore(bakDir);
            string f = Path.Combine(dir, "doc.txt");
            File.WriteAllText(f, "old\n");
            var b = new TextBuffer(null);
            b.Open(f);
            b.InsertChar(0, 0, 'N');
            b.Save(backup: store);
            Assert.False(File.Exists(f + ".bak"));
            string[] copies = Directory.GetFiles(bakDir, "*.bak");
            Assert.Single(copies);
            Assert.Equal("old\n", File.ReadAllText(copies[0]));
            Assert.StartsWith("Nold", File.ReadAllText(f));

            var b2 = new TextBuffer(null);
            b2.Open(f);
            b2.Save(backup: null);
            Assert.Single(Directory.GetFiles(bakDir, "*.bak"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RecentPopupShape()
    {
        var loc = Loc.Load("ru");
        var m = ModalState.Recent(loc, new List<string> { "a.txt", "b.txt", "c.txt" });
        Assert.Equal(ModalKind.Recent, m.Kind);
        Assert.False(m.Danger);
        Assert.Equal(3, m.Buttons.Count);
        Assert.Equal('1', m.Buttons[0].Hotkey);
        Assert.Equal('3', m.Buttons[2].Hotkey);
        Assert.StartsWith("[1]", m.Buttons[0].Label);

        var ten = Enumerable.Range(1, 10).Select(i => $"f{i}.txt").ToList();
        var m10 = ModalState.Recent(loc, ten);
        Assert.Equal('0', m10.Buttons[9].Hotkey);
        Assert.StartsWith("[0]", m10.Buttons[9].Label);

        var twelve = Enumerable.Range(1, 12).Select(i => $"f{i}.txt").ToList();
        var m12 = ModalState.Recent(loc, twelve);
        Assert.Equal(12, m12.Buttons.Count);
        Assert.Equal('\0', m12.Buttons[10].Hotkey);
        Assert.False(m12.Buttons[10].Label.StartsWith('['));
        // Текст без номера начинается в той же колонке, что после "[N] ".
        Assert.Equal(4, m12.Buttons[10].Label.IndexOf("f11.txt", StringComparison.Ordinal));
        Assert.Equal(5, m12.MaxVisibleButtons);
        Assert.Equal(0, m12.Selected);
        Assert.Equal(0, m12.ButtonTop);

        var mLong = ModalState.Recent(loc, new List<string> { new string('x', 60) });
        Assert.StartsWith("[1] ...", mLong.Buttons[0].Label);
        Assert.True(mLong.Buttons[0].Label.Length < 60);

        Assert.Throws<ArgumentException>(() => ModalState.Recent(loc, new List<string>()));
        Assert.Equal(1, ModalState.Recent(loc, ten).HandleKey(
            new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false)).Button);
    }

    [Fact]
    public void RecentPopupScroll()
    {
        var loc = Loc.Load("ru");
        var twelve = Enumerable.Range(1, 12).Select(i => $"f{i}.txt").ToList();
        var ms = ModalState.Recent(loc, twelve);
        for (int i = 0; i < 5; i++) ms.HandleKey(Arrow(ConsoleKey.DownArrow));
        Assert.Equal(5, ms.Selected);
        Assert.Equal(1, ms.ButtonTop);
        ms.HandleKey(Arrow(ConsoleKey.DownArrow));
        Assert.Equal(6, ms.Selected);
        Assert.Equal(2, ms.ButtonTop);
        ms.HandleKey(Arrow(ConsoleKey.UpArrow));
        Assert.Equal(5, ms.Selected);
        Assert.Equal(2, ms.ButtonTop);
        for (int i = 0; i < 5; i++) ms.HandleKey(Arrow(ConsoleKey.UpArrow));
        Assert.Equal(0, ms.Selected);
        Assert.Equal(0, ms.ButtonTop);
        ms.HandleKey(Arrow(ConsoleKey.End));
        Assert.Equal(11, ms.Selected);
        Assert.Equal(7, ms.ButtonTop);
        Assert.Equal(11, ms.HandleKey(Arrow(ConsoleKey.Enter)).Button);
        ms.HandleKey(Arrow(ConsoleKey.Home));
        Assert.Equal(0, ms.Selected);
        Assert.Equal(0, ms.ButtonTop);
    }
}
