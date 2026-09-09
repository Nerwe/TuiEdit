using System.Diagnostics;

namespace TuiEdit;

/// <summary>
/// Запуск git, который никогда не вешает вызывающего.
/// Контекст: git.exe без присоединённой консоли виснет навсегда, если вверх
/// от рабочей папки нет репозитория (порождает conhost и ждёт). Поэтому:
/// сначала пешком ищем <c>.git</c> (быстро и безопасно), спавн — только при
/// найденном корне, и всё равно с общим таймаутом, чтением обоих потоков
/// и убийством по таймауту.
/// </summary>
internal static class GitProcess
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Вверх от dir есть <c>.git</c> (папка, либо файл worktree/submodule)?
    /// Сомнения (нет доступа, странный путь, задан GIT_DIR) — true:
    /// пусть решает сам git, но уже ограниченный таймаутом ниже.
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

    /// <summary>Stdout при exit 0, иначе null. Общий таймаут на всё, убийство по нему.</summary>
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
            // Неинтерактивность: ни пейджеров, ни промптов, ни локов от нас.
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
