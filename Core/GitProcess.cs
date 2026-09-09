using System.Diagnostics;

namespace TuiEdit;

/// <summary>
/// Runs git without ever hanging the caller.
/// Context: git.exe without an attached console hangs forever when no repository
/// exists above the working folder (it spawns conhost and waits). Therefore:
/// first walks up to find <c>.git</c> (fast and safe), spawns only when a root
/// is found, and still uses a shared timeout, drains both streams, and kills
/// the process on timeout.
/// </summary>
internal static class GitProcess
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Determines whether a <c>.git</c> exists above dir (folder or worktree/submodule file)?
    /// Returns true when in doubt (no access, odd path, GIT_DIR set):
    /// lets git itself decide, already bounded by the timeout below.
    /// </summary>
    internal static bool HasRepoRoot(string dir)
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_DIR")))
                return true;
            string? cur = Path.GetFullPath(dir);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int depth = 0; depth < 64 && cur is not null && seen.Add(cur); depth++)
            {
                try
                {
                    if (File.Exists(Path.Combine(cur, ".git"))
                        || Directory.Exists(Path.Combine(cur, ".git")))
                        return true;
                }
                catch
                {
                    return true;
                }
                cur = Path.GetDirectoryName(cur);
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Returns stdout on exit 0, otherwise null. Applies a shared timeout to everything and kills the process on expiry.</summary>
    internal static string? Run(string dir, params string[] args)
    {
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            // Enforces non-interactive mode: no pagers, prompts, or locks from us.
            p.StartInfo.Environment["GIT_PAGER"] = "cat";
            p.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            p.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (string a in args)
                p.StartInfo.ArgumentList.Add(a);
            if (!p.Start())
                return null;
            try
            {
                p.StandardInput.Close();
            }
            catch
            {
            }
            Task<string> stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> stderr = p.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                p.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return null;
            }
            bool drained = false;
            try
            {
                drained = Task.WaitAll([stdout, stderr], Timeout);
            }
            catch
            {
            }
            if (!drained || !stdout.IsCompletedSuccessfully)
                return null;
            return p.ExitCode == 0 ? stdout.Result : null;
        }
        catch
        {
            return null;
        }
    }
}
