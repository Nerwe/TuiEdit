namespace TuiEdit;

/// <summary>Represents one changed hunk for review (0-based rows, public for tests).</summary>
internal sealed record ReviewHunk(string File, int StartRow, int EndRow, int Added, int Modified);

/// <summary>
/// Collects review hunks: contiguous changed-line ranges per file, current
/// file's hunks first. Pure except for the injected marks reader (unit-tested).
/// </summary>
internal static class ReviewHunks
{
    /// <summary>Groups sorted rows into contiguous ranges.</summary>
    public static List<(int Start, int End)> GroupRanges(IReadOnlySet<int> rows)
    {
        var ranges = new List<(int, int)>();
        int? start = null;
        int prev = -2;
        foreach (int r in rows.OrderBy(x => x))
        {
            if (start is null || r != prev + 1)
            {
                if (start is not null)
                    ranges.Add((start.Value, prev));
                start = r;
            }
            prev = r;
        }
        if (start is not null)
            ranges.Add((start.Value, prev));
        return ranges;
    }

    /// <summary>
    /// Collects hunks across files: the current file first, then the rest sorted.
    /// </summary>
    public static List<ReviewHunk> Collect(
        Func<string, (IReadOnlySet<int> Added, IReadOnlySet<int> Modified)> marksFor,
        string? currentFile,
        IEnumerable<string> files,
        int maxHunks = 200)
    {
        var hunks = new List<ReviewHunk>();
        var ordered = files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => string.Equals(f, currentFile, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal);
        foreach (string f in ordered)
        {
            if (hunks.Count >= maxHunks)
                break;
            (IReadOnlySet<int> added, IReadOnlySet<int> modified) = marksFor(f);
            if (added.Count == 0 && modified.Count == 0)
                continue;
            var union = new HashSet<int>(added);
            union.UnionWith(modified);
            foreach ((int s, int e) in GroupRanges(union))
            {
                if (hunks.Count >= maxHunks)
                    break;
                int a = 0, m = 0;
                for (int r = s; r <= e; r++)
                {
                    if (added.Contains(r)) a++;
                    else if (modified.Contains(r)) m++;
                }
                hunks.Add(new ReviewHunk(f, s, e, a, m));
            }
        }
        return hunks;
    }
}
