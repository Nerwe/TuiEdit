using System.Globalization;
using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Drafts and the restore popup.</summary>
[Trait("Category", "Integration")]
public sealed class DraftRestoreTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void KeyForUntitled(string? path)
    {
        Assert.Equal("untitled", DraftStore.KeyFor(path));
    }

    [Fact]
    public void KeyForStableSha1()
    {
        string p = Path.Combine(tmp.Path, "a.txt");
        string k1 = DraftStore.KeyFor(p);
        string k2 = DraftStore.KeyFor(p);
        Assert.Equal(k1, k2);
        Assert.Equal(40, k1.Length);
        Assert.NotEqual("untitled", k1);
    }

    [Fact]
    public void WriteReadDeleteRoundtrip()
    {
        string dir = tmp.NewDir();
        var ds = new DraftStore(dir);
        ds.Write(Path.Combine(dir, "a.txt"), new List<string> { "one", "two" }, 1, 2);
        ds.Write(null, new List<string> { "scratch" }, 0, 7);
        var all = ds.ReadAll();
        Assert.Equal(2, all.Count);
        var named = all.First(x => x.draft.File is not null);
        Assert.Equal(new List<string> { "one", "two" }, named.draft.Lines);
        Assert.Equal(1, named.draft.Row);
        Assert.Equal(2, named.draft.Col);
        Assert.Contains(all, x => x.draft.File is null && x.key == "untitled");
        ds.Delete(Path.Combine(dir, "a.txt"));
        Assert.Single(ds.ReadAll());
        ds.DeleteKey("untitled");
        Assert.Empty(ds.ReadAll());
        ds.Delete(Path.Combine(dir, "nope.txt"));
    }

    [Fact]
    public void EmergencyDumpWritesModifiedTabsOnly()
    {
        string dir = tmp.NewDir();
        string target = Path.Combine(dir, "work.txt");
        File.WriteAllText(target, "v1");
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        try
        {
            var buf = new TextBuffer(null);
            buf.Open(target);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine(dir, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            buf.InsertChar(0, 0, 'X'); // tab 1 is dirty
            ed.NewTab(); // tab 2 is clean
            Assert.Equal(1, ed.EmergencyDump());
            var all = ds.ReadAll();
            var hit = all.FirstOrDefault(x => target.Equals(x.draft.File, StringComparison.Ordinal));
            Assert.NotNull(hit.draft.File);
            Assert.StartsWith("Xv1", hit.draft.Lines[0]);
            Assert.Equal(1, all.Count(x => target.Equals(x.draft.File, StringComparison.Ordinal)));
        }
        finally
        {
            try { ds.Delete(target); } catch { }
        }
    }

    [Fact]
    public void BadJsonSkipped()
    {
        string dir = tmp.NewDir();
        var ds = new DraftStore(dir);
        ds.Write(Path.Combine(dir, "ok.txt"), new List<string> { "x" }, 0, 0);
        File.WriteAllText(Path.Combine(dir, "junk.json"), "{not json");
        Assert.Single(ds.ReadAll());
    }

    [Fact]
    public void DraftDue30s()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(TuiEditor.DraftDue(t0, t0.AddSeconds(29)));
        Assert.True(TuiEditor.DraftDue(t0, t0.AddSeconds(30)));
        Assert.True(TuiEditor.DraftDue(t0, t0.AddHours(1)));
    }

    [Fact]
    public void RestoreContent()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "a", "b" });
        Assert.Equal("a", b.GetLine(0));
        Assert.Equal("b", b.GetLine(1));
        Assert.True(b.IsModified);
        Assert.False(b.CanUndo);
        b.RestoreContent(new List<string>());
        Assert.Equal(1, b.Count);
        Assert.Equal("", b.GetLine(0));
    }

    [Fact]
    public void RestorePopupShape()
    {
        var loc = Loc.Load("ru");
        var when = new DateTime(2026, 9, 7, 10, 30, 0, DateTimeKind.Utc);
        var m = ModalState.Restore(loc, new List<(string, DateTime)> { ("a.txt", when), ("без имени", when) });
        Assert.Equal(ModalKind.Restore, m.Kind);
        Assert.False(m.Danger);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Equal('1', m.Buttons[0].Hotkey);
        Assert.Single(m.Lines);
        string wantDate = when.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        Assert.Contains(wantDate, m.Buttons[0].Label);
        Assert.Throws<ArgumentException>(() => ModalState.Restore(loc, new List<(string, DateTime)>()));
    }

    [Fact]
    public void RestoreAllButtonFirst()
    {
        var loc = Loc.Load("en");
        var when = DateTime.UtcNow;
        var m = ModalState.Restore(loc,
            new List<(string, DateTime)> { ("a.txt", when), ("b.txt", when) }, showRestoreAll: true);
        Assert.Equal(3, m.Buttons.Count);
        Assert.Equal('1', m.Buttons[0].Hotkey);
        Assert.Contains("2", m.Buttons[0].Label);
    }

    [Fact]
    public void AutoDraftSweepsAllTabs()
    {
        string dir = tmp.NewDir();
        string f1 = Path.Combine(dir, "one.txt"), f2 = Path.Combine(dir, "two.txt");
        File.WriteAllText(f1, "a");
        File.WriteAllText(f2, "b");
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        try
        {
            var buf = new TextBuffer(null);
            buf.Open(f1);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine(dir, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            buf.InsertChar(0, 0, 'X');
            ed.OpenStartupFile(f2, 0, 0);
            ActiveBuf(ed).InsertChar(0, 0, 'Y');
            SetLastDraft(ed, DateTime.MinValue);
            AutoDraft(ed);
            var all = ds.ReadAll();
            Assert.Contains(all, x => x.key == DraftStore.KeyFor(f1));
            Assert.Contains(all, x => x.key == DraftStore.KeyFor(f2));
            Assert.Equal("Xa", all.First(x => x.key == DraftStore.KeyFor(f1)).draft.Lines[0]);
        }
        finally
        {
            try { ds.Delete(f1); } catch { }
            try { ds.Delete(f2); } catch { }
        }
    }

    [Fact]
    public void UntitledDraftsDoNotCollide()
    {
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        var before = new HashSet<string>(ds.ReadAll()
            .Where(x => x.draft.File is null).Select(x => x.key));
        try
        {
            var ed = new TuiEditor(new TextBuffer(null), new AppSettings(),
                new SettingsStore(Path.Combine(tmp.Path, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            ActiveBuf(ed).InsertChar(0, 0, 'A');
            ed.NewTab();
            ActiveBuf(ed).InsertChar(0, 0, 'B');
            SetLastDraft(ed, DateTime.MinValue);
            AutoDraft(ed);
            var fresh = ds.ReadAll()
                .Where(x => x.draft.File is null && !before.Contains(x.key)).ToList();
            Assert.Equal(2, fresh.Count);
            Assert.NotEqual(fresh[0].key, fresh[1].key);
            Assert.Equal(new[] { "A", "B" },
                fresh.Select(x => x.draft.Lines[0]).Order().ToList());
            foreach (var x in fresh)
                ds.DeleteKey(x.key);
        }
        finally
        {
            foreach (var x in ds.ReadAll().Where(x => x.draft.File is null && !before.Contains(x.key)))
                try { ds.DeleteKey(x.key); } catch { }
        }
    }

    [Fact]
    public void RestoreAllIntoNewTabsKeepsCliBuffer()
    {
        string dir = tmp.NewDir();
        string cli = Path.Combine(dir, "cli.txt"), work = Path.Combine(dir, "work.txt");
        File.WriteAllText(cli, "CLI");
        File.WriteAllText(work, "v1");
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        try
        {
            ds.Write(work, new List<string> { "drafted" }, 0, 3);
            ds.Write(null, new List<string> { "scratch" }, 0, 1);
            var buf = new TextBuffer(null);
            buf.Open(cli);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine(dir, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            MaybeRestore(ed);
            var dlg = Dialog(ed);
            Assert.NotNull(dlg);
            dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
            Assert.Equal(3, ed.TabCount); // CLI tab plus both drafts, nothing overwritten
            var firstLines = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                ed.SwitchTab(i);
                firstLines.Add(ActiveBuf(ed).GetLine(0));
            }
            Assert.Equal(new[] { "CLI", "drafted", "scratch" }, firstLines.Order().ToList());
            Assert.Empty(ds.ReadAll().Where(x =>
                x.key == DraftStore.KeyFor(work) || x.key == "untitled"));
        }
        finally
        {
            try { ds.Delete(work); } catch { }
            try { ds.DeleteKey("untitled"); } catch { }
        }
    }

    [Fact]
    public void SingleRestoreLoopsRemainderDialog()
    {
        string dir = tmp.NewDir();
        string f1 = Path.Combine(dir, "one.txt"), f2 = Path.Combine(dir, "two.txt");
        File.WriteAllText(f1, "a");
        File.WriteAllText(f2, "b");
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        try
        {
            ds.Write(f1, new List<string> { "d1" }, 0, 0);
            ds.Write(f2, new List<string> { "d2" }, 0, 0);
            var ed = new TuiEditor(new TextBuffer(null), new AppSettings(),
                new SettingsStore(Path.Combine(dir, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            MaybeRestore(ed);
            var dlg = Dialog(ed);
            Assert.NotNull(dlg);
            dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            dlg.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
            Assert.Equal(1, ed.TabCount); // pristine startup tab reused
            var dlg2 = (ModalDialog)Dialog(ed)!;
            Assert.NotNull(dlg2);
            Assert.NotSame(dlg, dlg2); // remainder dialog re-shown
            dlg2.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
            Assert.Equal(2, ed.TabCount);
            Assert.Empty(ds.ReadAll().Where(x =>
                x.key == DraftStore.KeyFor(f1) || x.key == DraftStore.KeyFor(f2)));
        }
        finally
        {
            try { ds.Delete(f1); } catch { }
            try { ds.Delete(f2); } catch { }
        }
    }

    [Fact]
    public void SuppressRestoreDialogSkipsPicker()
    {
        string dir = tmp.NewDir();
        string cli = Path.Combine(dir, "cli.txt"), work = Path.Combine(dir, "work.txt");
        File.WriteAllText(cli, "CLI");
        File.WriteAllText(work, "v1");
        string ddir = tmp.NewDir();
        var ds = new DraftStore(ddir);
        try
        {
            ds.Write(work, new List<string> { "drafted" }, 0, 0);
            var buf = new TextBuffer(null);
            buf.Open(cli);
            var ed = new TuiEditor(buf, new AppSettings(),
                new SettingsStore(Path.Combine(dir, "s.json")));
            ed.SetDraftStore(new DraftStore(ddir));
            ed.SuppressRestoreDialog = true; // CLI files: edit, don't hijack startup
            MaybeRestore(ed);
            Assert.Null(Dialog(ed));
            // Drafts survive for a plain launch.
            Assert.Contains(ds.ReadAll(), x => x.key == DraftStore.KeyFor(work));
        }
        finally
        {
            try { ds.Delete(work); } catch { }
        }
    }

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(ed)!;

    private static void MaybeRestore(TuiEditor ed) =>
        typeof(TuiEditor).GetMethod("MaybeRestore",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ed, []);

    private static void AutoDraft(TuiEditor ed) =>
        typeof(TuiEditor).GetMethod("AutoDraft",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ed, []);

    private static void SetLastDraft(TuiEditor ed, DateTime at) =>
        typeof(TuiEditor).GetField("_lastDraftAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(ed, at);

    private static ModalDialog? Dialog(TuiEditor ed) =>
        typeof(TuiEditor).GetField("_dialog",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(ed) as ModalDialog;
}
