namespace TuiEdit;

/// <summary>
/// Построчные метки git diff для гуттера (добавлено/изменено, 0-based).
/// Как <see cref="GitStatus"/>: форк только фоном, Render читает кэш.
/// Чистые удаления (без строк с новой стороны) не маркируем.
/// </summary>
internal static class GitDiff
{
    /// <summary>Метки для файла (пусто — не репо/нет изменений/ошибка). Никогда не блокирует.</summary>
    public static (IReadOnlySet<int> added, IReadOnlySet<int> modified) MarksFor(string? filePath) =>
        GitService.Shared.DiffMarks(filePath);

    /// <summary>То же синхронно (для тестов).</summary>
    internal static (IReadOnlySet<int> added, IReadOnlySet<int> modified) MarksForSync(string? filePath) =>
        GitService.DiffMarksSync(filePath);

    /// <summary>Разбор `git diff --unified=0` (чистая, для тестов).</summary>
    internal static (HashSet<int> added, HashSet<int> modified) Parse(string? output)
    {
        var added = new HashSet<int>();
        var modified = new HashSet<int>();
        if (string.IsNullOrEmpty(output))
            return (added, modified);
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("@@ ", StringComparison.Ordinal))
                continue;
            if (!TryParseHunk(line, out int oldCount, out int newStart, out int newCount))
                continue;
            if (newCount <= 0 || newStart < 1)
                continue; // чистое удаление — не маркируем
            var target = oldCount == 0 ? added : modified;
            for (int r = newStart - 1; r < newStart - 1 + newCount; r++)
                target.Add(r);
        }
        return (added, modified);
    }

    /// <summary>Разбор `@@ -a[,b] +c[,d] @@` (нет запятой — счёт 1).</summary>
    private static bool TryParseHunk(string line, out int oldCount, out int newStart, out int newCount)
    {
        oldCount = 0;
        newStart = 0;
        newCount = 0;
        if (!TrySpan(line, " -", out _, out oldCount))
            return false;
        return TrySpan(line, " +", out newStart, out newCount);
    }

    private static bool TrySpan(string line, string marker, out int start, out int count)
    {
        start = 0;
        count = 0;
        int at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return false;
        int from = at + marker.Length;
        int end = line.IndexOf(' ', from);
        if (end < 0)
            end = line.IndexOf('@', from); // закрывающие `@@` без пробела
        string span = (end < 0 ? line[from..] : line[from..end]).Trim().TrimEnd('@').Trim();
        int comma = span.IndexOf(',');
        if (comma < 0)
        {
            if (!int.TryParse(span, out start))
                return false;
            count = 1;
            return true;
        }
        return int.TryParse(span[..comma], out start)
            && int.TryParse(span[(comma + 1)..], out count);
    }
}
