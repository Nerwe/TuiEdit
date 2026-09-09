using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

[Trait("Category", "Integration")]
public sealed class BackupStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly BackupStore _store;

    public BackupStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_bstore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new BackupStore(Path.Combine(_dir, "backups"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void DefaultDirSitsNextToSettings()
    {
        Assert.Equal(Path.Combine(_dir, "backups"),
            BackupStore.DefaultDir(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void RotationKeepsFiveNewest()
    {
        string f = Path.Combine(_dir, "doc.txt");
        File.WriteAllText(f, "v0");
        for (int i = 1; i <= 7; i++)
            _store.Write(f, System.Text.Encoding.UTF8.GetBytes("v" + i));
        string[] copies = Directory.GetFiles(_store.Dir, "*.bak");
        Assert.Equal(BackupStore.MaxPerFile, copies.Length);
        Assert.Equal(5, BackupStore.MaxPerFile);
        var contents = copies.Select(File.ReadAllText).ToHashSet();
        Assert.DoesNotContain("v1", contents);
        Assert.DoesNotContain("v2", contents);
        Assert.Contains("v7", contents);
    }

    [Fact]
    public void OlderThanWeekIsPruned()
    {
        string f = Path.Combine(_dir, "doc.txt");
        File.WriteAllText(f, "x");
        _store.Write(f, System.Text.Encoding.UTF8.GetBytes("old"));
        string aged = Assert.Single(Directory.GetFiles(_store.Dir, "*.bak"));
        File.SetLastWriteTime(aged, DateTime.Now - TimeSpan.FromDays(8));
        _store.Write(f, System.Text.Encoding.UTF8.GetBytes("new"));
        string[] copies = Directory.GetFiles(_store.Dir, "*.bak");
        Assert.Single(copies);
        Assert.Equal("new", File.ReadAllText(copies[0]));
    }

    [Fact]
    public void PruneAllSweepsEveryFile()
    {
        string a = Path.Combine(_dir, "a.txt");
        string b = Path.Combine(_dir, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        _store.Write(a, System.Text.Encoding.UTF8.GetBytes("old-a"));
        _store.Write(b, System.Text.Encoding.UTF8.GetBytes("fresh-b"));
        string aged = Directory.GetFiles(_store.Dir, "*.bak")
            .First(f => File.ReadAllText(f) == "old-a");
        File.SetLastWriteTime(aged, DateTime.Now - TimeSpan.FromDays(8));
        _store.PruneAll();
        string[] left = Directory.GetFiles(_store.Dir, "*.bak");
        Assert.Single(left);
        Assert.Equal("fresh-b", File.ReadAllText(left[0]));
    }
}
