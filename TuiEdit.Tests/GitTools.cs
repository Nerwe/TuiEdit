using System.Diagnostics;

namespace TuiEdit.Tests;

// Shared real-git scaffolding (soft-skip when git is missing).
internal static class GitTools
{
    /// <summary>Whether the git executable runs.</summary>
    public static bool HaveGit()
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

    /// <summary>Runs git, throwing when it fails (test setup only).</summary>
    /// <param name="dir">The working directory.</param>
    /// <param name="args">The git arguments.</param>
    public static void Run(string dir, string args)
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

    /// <summary>Inits a repo with one committed file; returns the dir, file via out.</summary>
    /// <param name="file">Receives the committed file path.</param>
    public static string InitRepo(out string file)
    {
        string dir = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "tui_git_" + Guid.NewGuid().ToString("N"))).FullName;
        Run(dir, "init -q");
        Run(dir, "-c user.email=t@t -c user.name=t commit -q --allow-empty -m init");
        file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "one");
        Run(dir, "add a.txt");
        Run(dir, "-c user.email=t@t -c user.name=t commit -q -m one");
        return dir;
    }
}
