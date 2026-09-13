namespace TuiEdit;

/// <summary>Provides indent-based folding: a block holds lines indented deeper than the start (blank lines belong to it).</summary>
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

    /// <summary>Finds the last line of a block (excludes trailing blank lines).</summary>
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

    /// <summary>Determines whether a line hides inside a fold (the start line always stays visible).</summary>
    public static bool IsHidden(IReadOnlyList<string> lines, SortedSet<int> folds, int row)
    {
        if (folds.Count == 0)
            return false;
        foreach (int f in folds)
        {
            if (f >= row)
                break;
            if (EndOf(lines, f) >= row)
                return true;
        }
        return false;
    }

    /// <summary>Removes folds containing a line (for jumps inside).</summary>
    public static void UnfoldContaining(IReadOnlyList<string> lines, SortedSet<int> folds, int row) =>
        folds.RemoveWhere(f => f < row && EndOf(lines, f) >= row);
}
