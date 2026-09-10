namespace TuiEdit;

internal sealed record SidebarEntry(string Name, bool IsDir, bool IsHidden = false, bool IsExe = false);

/// <summary>Provides the file sidebar as a lazy tree (fixed <see cref="Width"/> width).</summary>
internal sealed class SidebarState
{
    public const int Width = 24;

    private readonly SidebarTree _tree;
    private List<(SidebarNode node, int depth)> _rows = new();

    /// <summary>Gets the shown root (always full).</summary>
    public string CurrentDir => _tree.Root;

    /// <summary>Gets the visible rows (pre-order, root children at depth 0).</summary>
    public IReadOnlyList<(SidebarNode node, int depth)> Rows => _rows;

    public int Selected { get; private set; }

    /// <summary>Gets the first visible row (scroll).</summary>
    public int Top { get; private set; }

    private int _visCount = 10;

    public SidebarState(string root)
    {
        _tree = new SidebarTree(root);
        Rebuild(null);
    }

    /// <summary>Gets the highlighted row (null when empty).</summary>
    public (SidebarNode node, int depth)? Current =>
        _rows.Count == 0 ? null : _rows[Selected];

    /// <summary>Moves the highlight (keeps the visible window via visCount).</summary>
    public void MoveHighlight(int d, int visCount)
    {
        if (_rows.Count == 0)
            return;
        _visCount = Math.Max(1, visCount);
        Selected = Math.Clamp(Selected + d, 0, _rows.Count - 1);
        EnsureVisible();
    }

    /// <summary>Gets the full path of the highlighted row (null when empty).</summary>
    public string? SelectedPath => Current?.node.Path;

    public bool SelectedIsDir => Current?.node.IsDir == true;

    /// <summary>
    /// Handles Enter: expands/collapses a folder (stays on it); returns
    /// <see langword="true"/> on a folder, <see langword="false"/> on a file (the editor opens it).
    /// </summary>
    public bool EnterSelected()
    {
        var cur = Current;
        if (cur is null || !cur.Value.node.IsDir)
            return false;
        string keep = cur.Value.node.Path;
        _tree.Toggle(cur.Value.node);
        Rebuild(keep);
        return true;
    }

    /// <summary>Expands the highlighted folder (files are a no-op).</summary>
    public void ExpandSelected()
    {
        var cur = Current;
        if (cur is null || !cur.Value.node.IsDir || cur.Value.node.IsExpanded)
            return;
        string keep = cur.Value.node.Path;
        _tree.Toggle(cur.Value.node);
        Rebuild(keep);
    }

    /// <summary>Collapses the highlighted folder, else jumps to the parent.</summary>
    public void CollapseOrParent()
    {
        var cur = Current;
        if (cur is null)
            return;
        if (cur.Value.node.IsDir && cur.Value.node.IsExpanded)
        {
            string keep = cur.Value.node.Path;
            _tree.Toggle(cur.Value.node);
            Rebuild(keep);
            return;
        }
        if (cur.Value.depth > 0 && cur.Value.node.Parent is not null)
            Rebuild(cur.Value.node.Parent.Path);
    }

    /// <summary>Refreshes expanded folders from disk (keeps the highlight by path).</summary>
    public void Refresh()
    {
        string? keep = Current?.node.Path;
        _tree.RefreshExpanded();
        Rebuild(keep);
    }

    private void Rebuild(string? keepPath)
    {
        int top = Top;
        _rows = _tree.VisibleRows();
        Selected = 0;
        if (keepPath is not null)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].node.Path.Equals(keepPath, StringComparison.Ordinal))
                {
                    Selected = i;
                    break;
                }
            }
        }
        Top = Math.Clamp(top, 0, Math.Max(0, _rows.Count - 1));
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        if (_rows.Count == 0)
        {
            Selected = 0;
            Top = 0;
            return;
        }
        Selected = Math.Clamp(Selected, 0, _rows.Count - 1);
        if (Selected < Top)
            Top = Selected;
        else if (Selected >= Top + _visCount)
            Top = Selected - _visCount + 1;
    }

    /// <summary>Classifies an entry (delegates to <see cref="FileKinds"/>).</summary>
    internal static SidebarEntry Classify(string fullPath, bool isDir)
    {
        FileKind kind = FileKinds.Classify(fullPath, isDir);
        return new SidebarEntry(kind.Name, kind.IsDir, kind.IsHidden, kind.IsExe);
    }

    internal static bool IsHidden(string fullPath) => FileKinds.IsHidden(fullPath);
}
