namespace TuiEdit;

/// <summary>A node of the sidebar tree: file or directory with lazy children.</summary>
internal sealed class SidebarNode
{
    /// <summary>Gets the full path.</summary>
    public string Path { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets a value that indicates whether this is a directory.</summary>
    public bool IsDir { get; }

    /// <summary>Gets a value that indicates whether this is hidden.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets a value that indicates whether this is executable.</summary>
    public bool IsExe { get; }

    /// <summary>Gets the parent (null for the root).</summary>
    public SidebarNode? Parent { get; }

    /// <summary>Gets the loaded children (empty until expanded).</summary>
    public List<SidebarNode> Children { get; } = new();

    /// <summary>Gets a value that indicates whether children are shown.</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>Gets the directory mtime seen at load (change detection).</summary>
    public DateTime LoadedMtime { get; private set; } = DateTime.MinValue;

    internal SidebarNode(string path, FileKind kind, SidebarNode? parent)
    {
        Path = path;
        Name = kind.Name;
        IsDir = kind.IsDir;
        IsHidden = kind.IsHidden;
        IsExe = kind.IsExe;
        Parent = parent;
    }

    internal void SetChildren(List<SidebarNode> children, DateTime mtime)
    {
        Children.Clear();
        Children.AddRange(children);
        LoadedMtime = mtime;
        IsExpanded = true;
    }

    internal void Collapse()
    {
        Children.Clear();
        IsExpanded = false;
    }
}

/// <summary>
/// Lazy file tree for the sidebar: children load on expand (directories first,
/// natural sort, hidden entries kept but dimmed like the flat list), directory
/// mtime invalidates stale loads, ancestor cycles (symlink loops) refuse to expand.
/// </summary>
internal sealed class SidebarTree
{
    /// <summary>Gets the root directory (always full).</summary>
    public string Root { get; }

    /// <summary>Gets the root node (always expanded, never shown itself).</summary>
    public SidebarNode RootNode { get; }

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="SidebarTree"/> class.
    /// </summary>
    /// <param name="root">The root directory (broken paths yield an empty tree).</param>
    public SidebarTree(string root)
    {
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch
        {
            full = string.Empty;
        }
        Root = full;
        RootNode = new SidebarNode(full, new FileKind(NameOf(full), true, false, false), null);
        if (Directory.Exists(full))
        {
            _expanded.Add(full);
            LoadChildren(RootNode);
        }
    }

    /// <summary>Visible rows in pre-order: root children at depth 0.</summary>
    public List<(SidebarNode node, int depth)> VisibleRows()
    {
        var rows = new List<(SidebarNode, int)>();
        foreach (SidebarNode child in RootNode.Children)
            AppendVisible(child, 0, rows);
        return rows;
    }

    /// <summary>Toggles a directory (files are a no-op); returns whether it ended expanded.</summary>
    /// <param name="node">The node to toggle.</param>
    public bool Toggle(SidebarNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsDir)
            return false;
        if (node.IsExpanded)
        {
            PruneExpanded(node);
            node.Collapse();
            return false;
        }
        Expand(node);
        if (node.IsExpanded)
            _expanded.Add(node.Path);
        return node.IsExpanded;
    }

    /// <summary>Reloads the node children when the directory changed on disk.</summary>
    /// <param name="node">The directory node to refresh.</param>
    /// <returns><see langword="true"/> if children were reloaded.</returns>
    public bool Refresh(SidebarNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsDir || !node.IsExpanded)
            return false;
        if (ReadMtime(node.Path) == node.LoadedMtime)
            return false;
        LoadChildren(node);
        return true;
    }

    /// <summary>
    /// Refreshes the tree: the root level always relists (one cheap listing, so
    /// same-tick changes are never missed), deeper expanded levels reload when
    /// their mtime changed. Returns whether anything changed.
    /// </summary>
    public bool RefreshExpanded()
    {
        bool any = MergeChildren(RootNode);
        foreach (SidebarNode child in RootNode.Children)
            any |= RefreshSubtree(child);
        return any;
    }

    private bool RefreshSubtree(SidebarNode node)
    {
        bool any = false;
        if (!node.IsDir || !node.IsExpanded)
            return false;
        if (ReadMtime(node.Path) != node.LoadedMtime)
        {
            LoadChildren(node);
            return true;
        }
        foreach (SidebarNode child in node.Children)
            any |= RefreshSubtree(child);
        return any;
    }

    private void PruneExpanded(SidebarNode node)
    {
        _expanded.Remove(node.Path);
        string prefix = node.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        _expanded.RemoveWhere(p =>
            p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendVisible(SidebarNode node, int depth, List<(SidebarNode, int)> rows)
    {
        rows.Add((node, depth));
        if (!node.IsExpanded)
            return;
        foreach (SidebarNode child in node.Children)
            AppendVisible(child, depth + 1, rows);
    }

    private void Expand(SidebarNode node)
    {
        if (IsCyclic(node))
            return; // symlink loop back to an ancestor — refuse
        LoadChildren(node);
    }

    private static bool IsCyclic(SidebarNode node)
    {
        string target = ResolveLink(node.Path);
        for (SidebarNode? p = node.Parent; p is not null; p = p.Parent)
        {
            if (string.Equals(ResolveLink(p.Path), target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ResolveLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            string? link = info.LinkTarget;
            if (link is null)
                return info.FullName;
            string combined = Path.IsPathFullyQualified(link)
                ? link
                : Path.Combine(info.Parent?.FullName ?? path, link);
            return Path.GetFullPath(combined);
        }
        catch
        {
            return path;
        }
    }

    private void LoadChildren(SidebarNode node)
    {
        MergeChildren(node);
        // Re-apply expansion survived from before the reload.
        foreach (SidebarNode child in node.Children)
        {
            if (child.IsDir && _expanded.Contains(child.Path))
                LoadChildren(child);
        }
    }

    /// <summary>
    /// Rebuilds one level, reusing surviving nodes (their subtrees and expansion
    /// stay intact). Returns whether the child set changed (order included).
    /// </summary>
    private static bool MergeChildren(SidebarNode node)
    {
        var dirs = new List<SidebarNode>();
        var files = new List<SidebarNode>();
        try
        {
            foreach (string e in Directory.EnumerateFileSystemEntries(node.Path))
            {
                bool isDir;
                try
                {
                    isDir = Directory.Exists(e);
                }
                catch
                {
                    continue;
                }
                string full;
                try
                {
                    full = Path.GetFullPath(e);
                }
                catch
                {
                    continue;
                }
                var child = new SidebarNode(full, FileKinds.Classify(full, isDir), node);
                (isDir ? dirs : files).Add(child);
            }
        }
        catch
        {
            // Denied or vanished: keep whatever was collected (possibly empty).
        }
        SortNodes(dirs);
        SortNodes(files);
        dirs.AddRange(files);
        var existing = new Dictionary<string, SidebarNode>(StringComparer.OrdinalIgnoreCase);
        foreach (SidebarNode c in node.Children)
            existing[c.Path] = c;
        var merged = new List<SidebarNode>(dirs.Count);
        foreach (SidebarNode fresh in dirs)
        {
            if (existing.TryGetValue(fresh.Path, out SidebarNode? old)
                && old.IsDir == fresh.IsDir)
                merged.Add(old); // reuse: subtree, expansion and mtime survive
            else
                merged.Add(fresh);
        }
        bool changed = merged.Count != node.Children.Count;
        for (int i = 0; i < merged.Count && !changed; i++)
        {
            changed = !merged[i].Path.Equals(node.Children[i].Path, StringComparison.OrdinalIgnoreCase)
                || merged[i].IsDir != node.Children[i].IsDir;
        }
        node.SetChildren(merged, ReadMtime(node.Path));
        return changed;
    }

    private static void SortNodes(List<SidebarNode> nodes) =>
        nodes.Sort((a, b) => NaturalSort.Compare(a.Name, b.Name));

    private static DateTime ReadMtime(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string NameOf(string full)
    {
        try
        {
            return Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return full;
        }
    }
}
