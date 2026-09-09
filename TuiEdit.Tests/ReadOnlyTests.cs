using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

[Trait("Category", "Integration")]
public sealed class ReadOnlyTests : IDisposable
{
    private readonly string _dir;

    public ReadOnlyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_ro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            foreach (string f in Directory.GetFiles(_dir))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void OpenDetectsReadOnlyAndSaveRefuses()
    {
        string f = Path.Combine(_dir, "ro.txt");
        File.WriteAllText(f, "x");
        File.SetAttributes(f, FileAttributes.ReadOnly);
        try
        {
            var b = new TextBuffer(f);
            Assert.True(b.IsReadOnly);
            b.InsertChar(0, 0, 'y');
            var ex = Assert.Throws<InvalidOperationException>(() => b.Save());
            Assert.Equal("ReadOnly", ex.Message);
            Assert.Equal("x", File.ReadAllText(f).Trim());
        }
        finally
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }
    }

    [Fact]
    public void WritableFileIsNotReadOnly()
    {
        string f = Path.Combine(_dir, "rw.txt");
        File.WriteAllText(f, "x");
        var b = new TextBuffer(f);
        Assert.False(b.IsReadOnly);
        b.Clear();
        Assert.False(b.IsReadOnly);
    }
}
