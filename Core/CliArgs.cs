namespace TuiEdit;

/// <summary>Разбор файлового аргумента вида path[:line] / path:$ (строка с конца — в конец).</summary>
internal static class CliArgs
{
    /// <returns>Путь и 1-based строка (0 — без перехода, int.MaxValue — в конец).</returns>
    public static (string Path, int Line) SplitFileLine(string arg)
    {
        int c = arg.LastIndexOf(':');
        if (c > 0 && c < arg.Length - 1)
        {
            string tail = arg[(c + 1)..];
            if (tail == "$")
                return (arg[..c], int.MaxValue);
            if (int.TryParse(tail, out int n) && n > 0)
                return (arg[..c], n);
        }
        return (arg, 0);
    }
}
