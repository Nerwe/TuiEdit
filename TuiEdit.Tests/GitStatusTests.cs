using System.Diagnostics;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Git-сегмент статусбара: ветка и грязь tracked-файлов.</summary>
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
    public void NullAndNonRepoGiveNull()
    {
        Assert.Null(GitStatus.ForFile(null));
        Assert.Null(GitStatus.ForFile(""));
        string dir = NewDir();
        try
        {
            Assert.Null(GitStatus.ForFile(Path.Combine(dir, "x.txt")));
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
            string? seg = GitStatus.ForFile(file);
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
            File.AppendAllText(file, "two"); // tracked правка — отдельный репо, кэш чист
            string? seg = GitStatus.ForFile(file);
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
            // Только untracked — по решению не считаем грязью (скорость status).
            string? seg = GitStatus.ForFile(Path.Combine(dir, "new.txt"));
            Assert.NotNull(seg);
            Assert.DoesNotContain("*", seg, StringComparison.Ordinal);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
