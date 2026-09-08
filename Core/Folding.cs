namespace TuiEdit;

/// <summary>Свёртки по отступу: блок — строки с отступом глубже стартовой (пустые принадлежат).</summary>
internal static class Folding
{
    public static int IndentOf(string line)
    {
        int n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t'))
            n++;
        return n;
    }

    public static bool CanFold(IReadOnlyList<string> lines, int row) => EndOf(lines, row) > row;

    /// <summary>Последняя строка блока (без висячих пустых в конце).</summary>
    public static int EndOf(IReadOnlyList<string> lines, int row)
    {
        if (row < 0 || row >= lines.Count)
            return row;
        if (lines[row].Trim().Length == 0)
            return row;
        int baseIndent = IndentOf(lines[row]);
        int end = row;
        for (int r = row + 1; r < lines.Count; r++)
        {
            if (lines[r].Trim().Length == 0)
            {
                end = r;
                continue;
            }
            if (IndentOf(lines[r]) > baseIndent)
            {
                end = r;
                continue;
            }
            break;
        }
        while (end > row && lines[end].Trim().Length == 0)
            end--;
        return end;
    }

    /// <summary>Строка скрыта свёрткой (стартовая видима всегда).</summary>
    public static bool IsHidden(IReadOnlyList<string> lines, SortedSet<int> folds, int row)
    {
        foreach (int f in folds)
        {
            if (f >= row)
                break;
            if (EndOf(lines, f) >= row)
                return true;
        }
        return false;
    }

    /// <summary>Убрать свёртки, содержащие строку (для прыжков внутрь).</summary>
    public static void UnfoldContaining(IReadOnlyList<string> lines, SortedSet<int> folds, int row) =>
        folds.RemoveWhere(f => f < row && EndOf(lines, f) >= row);
}
