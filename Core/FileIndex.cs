namespace TuiEdit;

/// <summary>
/// Lists project files for quick-open: recurses, skips hidden entries,
/// reads names only (not contents) for speed. Uses natural sorting.
/// </summary>
internal static class FileIndex
{
    public const int MaxFiles = 5000;

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
        try
        {
            foreach (string f in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                if (files.Count >= maxFiles)
                    break;
                if (Grep.IsHiddenUnder(f, rootFull))
                    continue;
                files.Add(f);
            }
        }
        catch
        {
        }
        files.Sort(NaturalSort.Comparer);
        return files;
    }
}
