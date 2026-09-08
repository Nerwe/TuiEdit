using System.Diagnostics;

namespace TuiEdit;

/// <summary>
/// Ветка и грязь git для статусбара: «⎇ main*» (грязь — только tracked,
/// untracked пропускаем ради скорости). Вне репо и при любых ошибках — null.
/// Дорогие вызовы (fork+exec, status на больших репо) прячем за TTL-кэшем:
/// Render зовёт это на каждый кадр, процесс плодить нельзя.
/// </summary>
internal static class GitStatus
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcTimeout = TimeSpan.FromSeconds(2);

    private static readonly object Gate = new();
    private static string? _dir; // папка, для которой посчитано
    private static Info? _cached; // null — тоже кэшируем (не репо), чтобы не форкать зря
    private static DateTime _at = DateTime.MinValue;

    /// <summary>Сегмент статусбара для файла («⎇ main*») или null.</summary>
    public static string? ForFile(string? filePath)
    {
        string? dir;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(dir))
            return null;
        lock (Gate)
        {
            if (dir != _dir || DateTime.UtcNow - _at >= Ttl)
            {
                _cached = Query(dir);
                _dir = dir;
                _at = DateTime.UtcNow;
            }
            return _cached is null ? null : Segment(_cached.Branch, _cached.Dirty);
        }
    }

    private static string Segment(string? branch, bool dirty) =>
        $"⎇ {(string.IsNullOrEmpty(branch) ? "?" : branch)}{(dirty ? "*" : string.Empty)}";

    private sealed record Info(string Root, string Branch, bool Dirty);

    private static Info? Query(string dir)
    {
        string? root = Run("rev-parse", "--show-toplevel", dir);
        if (string.IsNullOrWhiteSpace(root))
            return null;
        string? branch = Run("rev-parse", "--abbrev-ref HEAD", dir);
        string? porcelain = Run("status", "--porcelain=v1 --untracked-files=no", dir);
        if (porcelain is null)
            return null;
        return new Info(root.Trim(), (branch ?? "?").Trim(), porcelain.Length > 0);
    }

    private static string? Run(string command, string args, string dir)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git", $"{command} {args}")
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (!p.Start())
                return null;
            // Читаем вывод до ожидания: иначе deadlock при переполнении буфера.
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit((int)ProcTimeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null; // нет git в PATH, не репо, нет прав — просто без сегмента
        }
    }
}
