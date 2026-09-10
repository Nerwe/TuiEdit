using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Repo-wide change sets for tree/file views: tracked modifications plus untracked
// files, folders aggregating descendants by path prefix.
[Trait("Category", "Integration")]
public sealed class GitTreeTests
{
    [Fact]
    public void OutsideRepoIsEmpty()
    {
        var svc = new GitService();
        var (added, modified) = svc.ChangedFilesSync(Path.GetTempPath());
        Assert.Empty(added);
        Assert.Empty(modified);
    }

    [Fact]
    public void RepoRootResolves()
    {
        if (!GitTools.HaveGit())
            return;
        string dir = GitTools.InitRepo(out string file);
        try
        {
            Assert.Equal(
                Path.GetFullPath(dir),
                GitProcess.RepoRoot(Path.GetDirectoryName(file)!));
            Assert.Null(GitProcess.RepoRoot(Path.GetTempPath()));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void TracksModifiedAndUntracked()
    {
        if (!GitTools.HaveGit())
            return;
        string dir = GitTools.InitRepo(out string file);
        try
        {
            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            string added = Path.Combine(sub, "new.txt");
            File.WriteAllText(added, "new");
            File.AppendAllText(file, "dirty");

            var svc = new GitService();
            var (addedSet, modifiedSet) = svc.ChangedFilesSync(dir);
            Assert.Contains(Path.GetFullPath(added), addedSet);
            Assert.Contains(Path.GetFullPath(file), modifiedSet);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void SidebarAggregatesFolders()
    {
        if (!GitTools.HaveGit())
            return;
        string dir = GitTools.InitRepo(out string file);
        try
        {
            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            string added = Path.Combine(sub, "new.txt");
            File.WriteAllText(added, "new");
            File.AppendAllText(file, "dirty");

            var git = new GitService();
            var sb = new SidebarState(dir, git);
            // Prime the cache synchronously (background refresh is async).
            git.ChangedFilesSync(dir);

            SidebarNode sub0 = sb.Rows.First(r => r.node.Name == "sub").node;
            var (subAdded, subModified) = sb.GitMark(sub0);
            Assert.True(subAdded); // untracked descendant
            Assert.False(subModified);

            SidebarNode file0 = sb.Rows.First(r => r.node.Name == "a.txt").node;
            var (fileAdded, fileModified) = sb.GitMark(file0);
            Assert.False(fileAdded);
            Assert.True(fileModified);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
