using System.Diagnostics;

namespace TuiEdit;

/// <summary>
/// Ветка и грязь git для статусбара: «⎇ main*» (грязь — только tracked,
/// untracked пропускаем ради скорости). Вне репо и при любых ошибках — null.
/// Git вызывается ФОНОМ (один спавн status -sb за обновление): синхронные
/// форки в Render давали заминку ~100мс каждые 5с даже на маленьком репо,
/// а на большом — секунды. Render читает только последнее известное.
/// </summary>
internal static class GitStatus
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcTimeout = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static string? _dir; // папка, для которой посчитано
    private static Info? _cached; // null — тоже кэшируем (не репо), чтобы не форкать зря
    private static DateTime _at = DateTime.MinValue;
    private static bool _refreshing;

    /// <summary>Сегмент статусбара для файла («⎇ main*») или null. Никогда не блокирует.</summary>
    public static string? ForFile(string? filePath)
    {
        string? dir = DirOf(filePath);
        if (dir is null)
            return null;
        lock (Gate)
        {
            if (dir != _dir)
            {
                _dir = dir;
                _cached = null;
                _at = DateTime.MinValue; // новое место — сразу обновить
            }
            if (DateTime.UtcNow - _at >= Ttl && !_refreshing)
            {
                _refreshing = true;
                Task.Run(() =>
                {
                    try
                    {
                        Info? fresh = QueryCombined(dir);
                        lock (Gate)
                        {
                            if (dir == _dir)
                            {
                                _cached = fresh;
                                _at = DateTime.UtcNow;
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        lock (Gate)
                        {
                            _refreshing = false;
                        }
                    }
                });
            }
            return _cached is null ? null : Segment(_cached.Branch, _cached.Dirty);
        }
    }

    /// <summary>То же синхронно (для тестов; тоже кладёт в кэш).</summary>
    internal static string? ForFileSync(string? filePath)
    {
        string? dir = DirOf(filePath);
        if (dir is null)
            return null;
        Info? fresh = QueryCombined(dir);
        lock (Gate)
        {
            _dir = dir;
            _cached = fresh;
            _at = DateTime.UtcNow;
        }
        return fresh is null ? null : Segment(fresh.Branch, fresh.Dirty);
    }

    private static string? DirOf(string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            return Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch
        {
            return null;
        }
    }

    private static string Segment(string? branch, bool dirty) =>
        $"⎇ {(string.IsNullOrEmpty(branch) ? "?" : branch)}{(dirty ? "*" : string.Empty)}";

    private sealed record Info(string Branch, bool Dirty);

    /// <summary>Один спавн: ветка из заголовка ##, грязь — непустые строки ниже.</summary>
    private static Info? QueryCombined(string dir)
    {
        string? output = Run("status", "-sb --porcelain=v1 --untracked-files=no", dir);
        if (output is null)
            return null;
        string[] lines = output.Split('\n');
        string branch = "?";
        string head = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        const string unborn = "No commits yet on ";
        if (head.StartsWith("## ", StringComparison.Ordinal))
        {
            string rest = head[3..];
            if (rest.StartsWith(unborn, StringComparison.Ordinal))
            {
                branch = rest[unborn.Length..];
            }
            else
            {
                int end = rest.IndexOfAny(['.', ' ']);
                branch = (end < 0 ? rest : rest[..end]).Trim();
            }
        }
        bool dirty = false;
        for (int i = 1; i < lines.Length; i++)
            if (lines[i].Length > 0)
            {
                dirty = true;
                break;
            }
        return new Info(branch, dirty);
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
