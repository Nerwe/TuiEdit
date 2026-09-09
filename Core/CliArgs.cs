namespace TuiEdit;

/// <summary>Разбор файлового аргумента вида path[:line[:col]] / path:$ (строка с конца — в конец).</summary>
internal static class CliArgs
{
    /// <returns>Путь, 1-based строка (0 — без перехода, int.MaxValue — в конец) и 1-based колонка (0 — не двигать).</returns>
    public static (string Path, int Line, int Col) SplitFileLine(string arg)
    {
        int c = arg.LastIndexOf(':');
        if (c > 0 && c < arg.Length - 1)
        {
            string tail = arg[(c + 1)..];
            if (tail == "$")
                return (arg[..c], int.MaxValue, 0);
            string head = arg[..c];
            int c2 = head.LastIndexOf(':');
            if (c2 > 0 && c2 < head.Length - 1
                && int.TryParse(head[(c2 + 1)..], out int line) && line > 0
                && int.TryParse(tail, out int col) && col > 0)
                return (head[..c2], line, col);
            if (int.TryParse(tail, out int n) && n > 0)
                return (head, n, 0);
        }
        return (arg, 0, 0);
    }
}
