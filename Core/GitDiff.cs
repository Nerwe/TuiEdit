namespace TuiEdit;

/// <summary>
/// Provides per-line git diff marks for the gutter (added/modified, 0-based).
/// Behaves like <see cref="GitStatus"/>: forks only in the background, Render reads the cache.
/// Leaves pure deletions (no lines on the new side) unmarked.
/// </summary>
internal static class GitDiff
{
    /// <summary>Gets marks for a file (empty when not a repo, unchanged, or on error). Never blocks.</summary>
    public static (IReadOnlySet<int> added, IReadOnlySet<int> modified) MarksFor(string? filePath) =>
        GitService.Shared.DiffMarks(filePath);

    /// <summary>Gets the same marks synchronously (for tests).</summary>
    internal static (IReadOnlySet<int> added, IReadOnlySet<int> modified) MarksForSync(string? filePath) =>
        GitService.DiffMarksSync(filePath);

    /// <summary>Parses `git diff --unified=0` output (pure, for tests).</summary>
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
                continue; // Pure deletion — leaves unmarked
            var target = oldCount == 0 ? added : modified;
            for (int r = newStart - 1; r < newStart - 1 + newCount; r++)
                target.Add(r);
        }
        return (added, modified);
    }

    /// <summary>Parses `@@ -a[,b] +c[,d] @@` (no comma means a count of 1).</summary>
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
            end = line.IndexOf('@', from); // Closing `@@` without a space
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
