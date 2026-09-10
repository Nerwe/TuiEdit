using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Pure file operations: success paths plus every failure kind, never throws.
[Trait("Category", "Integration")]
public sealed class FileOpsTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void CreateFileOkAndExists()
    {
        string dir = tmp.NewDir("cf");
        string f = Path.Combine(dir, "a.txt");
        FileOpResult r = FileOps.CreateFile(f);
        Assert.True(r.Ok);
        Assert.Equal(FileOpError.None, r.Error);
        Assert.True(File.Exists(f));

        FileOpResult again = FileOps.CreateFile(f);
        Assert.False(again.Ok);
        Assert.Equal(FileOpError.AlreadyExists, again.Error);

        FileOpResult overDir = FileOps.CreateFile(dir);
        Assert.Equal(FileOpError.AlreadyExists, overDir.Error);
    }

    [Fact]
    public void CreateFileBadPath()
    {
        Assert.Equal(FileOpError.InvalidName, FileOps.CreateFile("").Error);
        Assert.Equal(FileOpError.InvalidName, FileOps.CreateFile("   ").Error);
    }

    [Fact]
    public void CreateDirectoryNested()
    {
        string dir = tmp.NewDir("cd");
        string deep = Path.Combine(dir, "a", "b");
        FileOpResult r = FileOps.CreateDirectory(deep);
        Assert.True(r.Ok);
        Assert.True(Directory.Exists(deep));

        Assert.Equal(FileOpError.AlreadyExists, FileOps.CreateDirectory(deep).Error);
    }

    [Fact]
    public void RenameOk()
    {
        string dir = tmp.NewDir("rn");
        string f = Path.Combine(dir, "old.txt");
        File.WriteAllText(f, "x");
        FileOpResult r = FileOps.Rename(f, "new.txt");
        Assert.True(r.Ok);
        Assert.True(File.Exists(Path.Combine(dir, "new.txt")));
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void RenameRejects()
    {
        string dir = tmp.NewDir("rn2");
        string f = Path.Combine(dir, "a.txt");
        File.WriteAllText(f, "x");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "y");

        Assert.Equal(FileOpError.NotFound, FileOps.Rename(Path.Combine(dir, "nope.txt"), "x.txt").Error);
        Assert.Equal(FileOpError.AlreadyExists, FileOps.Rename(f, "b.txt").Error);
        Assert.Equal(FileOpError.InvalidName, FileOps.Rename(f, "").Error);
        Assert.Equal(FileOpError.InvalidName, FileOps.Rename(f, "  ").Error);
        Assert.Equal(FileOpError.InvalidName, FileOps.Rename(f, "..").Error);
        Assert.Equal(FileOpError.InvalidName, FileOps.Rename(f, "a/b").Error);
        Assert.True(File.Exists(f)); // failed rename leaves the source alone
    }

    [Fact]
    public void DeleteFileDirMissing()
    {
        string dir = tmp.NewDir("del");
        string f = Path.Combine(dir, "a.txt");
        File.WriteAllText(f, "x");
        string sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "b.txt"), "y");

        Assert.Equal(1, FileOps.CountItems(f));
        Assert.Equal(1, FileOps.CountItems(sub)); // single file inside
        FileOpResult rf = FileOps.Delete(f);
        Assert.True(rf.Ok);
        Assert.False(File.Exists(f));

        FileOpResult rd = FileOps.Delete(sub);
        Assert.True(rd.Ok);
        Assert.False(Directory.Exists(sub));

        Assert.Equal(FileOpError.NotFound, FileOps.Delete(Path.Combine(dir, "gone.txt")).Error);
        Assert.Equal(0, FileOps.CountItems(Path.Combine(dir, "gone.txt")));
    }

    [Fact]
    public void ValidNameRules()
    {
        Assert.True(FileOps.ValidName("a.txt"));
        Assert.True(FileOps.ValidName("dir-name_2"));
        Assert.False(FileOps.ValidName(null));
        Assert.False(FileOps.ValidName(""));
        Assert.False(FileOps.ValidName("   "));
        Assert.False(FileOps.ValidName("."));
        Assert.False(FileOps.ValidName(".."));
        Assert.False(FileOps.ValidName("a/b"));
        if (OperatingSystem.IsWindows())
            Assert.False(FileOps.ValidName("a\\b")); // separator on Windows only
        else
            Assert.True(FileOps.ValidName("a\\b")); // ordinary char on Linux
    }
}
