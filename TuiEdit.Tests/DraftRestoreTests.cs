using System.Globalization;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Черновики и попап восстановления.</summary>
public sealed class DraftRestoreTests
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
        string dir = Path.Combine(Path.GetTempPath(), "tui_dr_" + Guid.NewGuid().ToString("N"));
        string k1 = DraftStore.KeyFor(Path.Combine(dir, "a.txt"));
        string k2 = DraftStore.KeyFor(Path.Combine(dir, "a.txt"));
        Assert.Equal(k1, k2);
        Assert.Equal(40, k1.Length);
        Assert.NotEqual("untitled", k1);
    }

    [Fact]
    public void WriteReadDeleteRoundtrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_dr_" + Guid.NewGuid().ToString("N"));
        try
        {
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
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BadJsonSkipped()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_dr_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ds = new DraftStore(dir);
            ds.Write(Path.Combine(dir, "ok.txt"), new List<string> { "x" }, 0, 0);
            File.WriteAllText(Path.Combine(dir, "junk.json"), "{not json");
            Assert.Single(ds.ReadAll());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
}
