using System.Diagnostics;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Git status-bar segment: branch and tracked-file dirt.</summary>
[Trait("Category", "Integration")]
public sealed class GitStatusTests
{
    private static bool HaveGit()
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            return p.Start() && p.WaitForExit(5000) && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NewDir() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "tui_git_" + Guid.NewGuid().ToString("N"))).FullName;

    private static void Git(string dir, string args)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        if (!p.Start() || !p.WaitForExit(15000) || p.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed");
    }

    private static string InitRepo(out string file)
    {
        string dir = NewDir();
        Git(dir, "init -q");
        Git(dir, "-c user.email=t@t -c user.name=t commit -q --allow-empty -m init");
        file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "one");
        Git(dir, "add a.txt");
        Git(dir, "-c user.email=t@t -c user.name=t commit -q -m one");
        return dir;
    }

    [Fact]
    public void AsyncCacheAgreesWithSync()
    {
        if (!HaveGit())
            return; // git optional for tests
        string dir = InitRepo(out string file);
        try
        {
            string? want = GitStatus.ForFileSync(file);
            Assert.NotNull(want);
            // Cache is fresh — the async path returns the same without background polling.
            Assert.Equal(want, GitStatus.ForFile(file));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void HasRepoRootWalksUp()
    {
        string dir = NewDir();
        try
        {
            Assert.False(GitProcess.HasRepoRoot(dir)); // empty — false at once, no spawn
            Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
            Assert.False(GitProcess.HasRepoRoot(Path.Combine(dir, "sub", "deep")));
            Directory.CreateDirectory(Path.Combine(dir, ".git"));
            Assert.True(GitProcess.HasRepoRoot(Path.Combine(dir, "sub", "deep")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void HasRepoRootSeesWorktreeFile()
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: elsewhere");
            Assert.True(GitProcess.HasRepoRoot(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void NullAndNonRepoGiveNull()
    {
        Assert.Null(GitStatus.ForFileSync(null));
        Assert.Null(GitStatus.ForFileSync(""));
        string dir = NewDir();
        try
        {
            Assert.Null(GitStatus.ForFileSync(Path.Combine(dir, "x.txt")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CleanRepoShowsBranchWithoutStar()
    {
        if (!HaveGit())
            return; // git optional for tests
        string dir = InitRepo(out string file);
        try
        {
            string? seg = GitStatus.ForFileSync(file);
            Assert.NotNull(seg);
            Assert.StartsWith("⎇ ", seg);
            Assert.DoesNotContain("*", seg, StringComparison.Ordinal);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void DirtyTrackedFileShowsStar()
    {
        if (!HaveGit())
            return; // git optional for tests
        string dir = InitRepo(out string file);
        try
        {
            File.AppendAllText(file, "two"); // tracked edit — separate repo, cache clean
            string? seg = GitStatus.ForFileSync(file);
            Assert.NotNull(seg);
            Assert.StartsWith("⎇ ", seg);
            Assert.EndsWith("*", seg);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void UntrackedOnlyIsNotDirty()
    {
        if (!HaveGit())
            return; // git optional for tests
        string dir = InitRepo(out _);
        try
        {
            // Untracked only — by design not counted as dirty (status speed).
            string? seg = GitStatus.ForFileSync(Path.Combine(dir, "new.txt"));
            Assert.NotNull(seg);
            Assert.DoesNotContain("*", seg, StringComparison.Ordinal);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
