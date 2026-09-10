namespace TuiEdit;

/// <summary>
/// Lists project files for quick-open: recurses, skips hidden entries,
/// reads names only (not contents) for speed. Uses natural sorting.
/// </summary>
internal static class FileIndex
{
    public const int MaxFiles = 5000;

    /// <summary>Traversal depth cap: bounds symlink cycles and absurd nesting.</summary>
    private const int MaxDepth = 64;

    public static List<string> EnumerateFiles(string root, int maxFiles = MaxFiles)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || maxFiles <= 0)
            return files;
        string rootFull;
        try
        {
            rootFull = Path.GetFullPath(root);
            if (!Directory.Exists(rootFull))
                return files;
        }
        catch
        {
            return files;
        }
        // Breadth-first by hand: never descends into hidden dirs (no .git/object
        // crawl on monorepos) and skips denied directories instead of aborting
        // the whole walk like AllDirectories does. Cap applies top-down, then sort.
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootFull };
            var dirs = new Stack<(string path, int depth)>();
            dirs.Push((rootFull, 0));
            while (dirs.Count > 0 && files.Count < maxFiles)
            {
                var (dir, depth) = dirs.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dir);
                }
                catch
                {
                    continue; // denied or vanished — keep walking the siblings
                }
                foreach (string e in entries)
                {
                    if (Grep.IsHiddenUnder(e, rootFull))
                        continue;
                    bool isDir;
                    string full;
                    try
                    {
                        isDir = Directory.Exists(e);
                        full = Path.GetFullPath(e);
                    }
                    catch
                    {
                        continue;
                    }
                    if (isDir)
                    {
                        // Lexical dedupe plus depth cap: symlink loops can grow
                        // ever-longer paths that never repeat textually.
                        if (depth + 1 < MaxDepth && seen.Add(full))
                            dirs.Push((e, depth + 1));
                    }
                    else if (files.Count < maxFiles)
                        files.Add(e);
                }
            }
        }
        catch
        {
        }
        files.Sort(NaturalSort.Comparer);
        return files;
    }
}
