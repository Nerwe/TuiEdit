namespace TuiEdit;

/// <summary>
/// Represents a document tab: buffer plus view state (cursor, scroll, selection).
/// The active tab mirrors that state in editor fields (cache);
/// switching tabs saves/restores the state (see TuiEditor).
/// </summary>
internal sealed class DocTab
{
    public TextBuffer Buf { get; }

    public int Row;
    public int Col;
    public int DesiredCol;
    public int Top;
    public int Left;
    public int TopSeg;

    public int AnchorRow;
    public int AnchorCol;
    public bool SelActive;

    public DocTab(TextBuffer buf)
    {
        Buf = buf;
    }

    /// <summary>Gets the tab syntax highlighting (the cache invalidates on buffer version changes).</summary>
    public SyntaxHighlighter Highlighter { get; } = new();

    /// <summary>Gets bookmarks: lines (0-based). Shifts via Shift/Drop/Move on edits.</summary>
    public SortedSet<int> Bookmarks { get; } = new();

    /// <summary>Gets folds: block start lines. Ends derive from indentation.</summary>
    public SortedSet<int> Folds { get; } = new();

    /// <summary>Toggles the bookmark on a line.</summary>
    public bool ToggleBookmark(int row)
    {
        if (!Bookmarks.Add(row))
        {
            Bookmarks.Remove(row);
            return false;
        }
        return true;
    }

    /// <summary>Shifts bookmarks starting at a line (for line insertions/deletions).</summary>
    public void ShiftBookmarks(int fromRow, int delta) => ShiftSet(Bookmarks, fromRow, delta);

    /// <summary>Removes bookmarks in the [first, last] range (deleted lines).</summary>
    public void DropBookmarks(int first, int last) => DropSet(Bookmarks, first, last);

    /// <summary>Moves bookmarks along with the moved block [s, e] (dir -1 moves up, +1 moves down).</summary>
    public void MoveBookmarks(int s, int e, int dir) => MoveSet(Bookmarks, s, e, dir);

    /// <summary>Discards bookmarks outside the document (after undo etc.).</summary>
    public void ClampBookmarks(int count) => Bookmarks.RemoveWhere(r => r < 0 || r >= count);

    /// <summary>Shifts bookmarks and fold starts together (single call for paired edits).</summary>
    public void ShiftMarks(int fromRow, int delta)
    {
        ShiftBookmarks(fromRow, delta);
        ShiftFolds(fromRow, delta);
    }

    /// <summary>Drops bookmarks and fold starts in [first, last] together.</summary>
    public void DropMarks(int first, int last)
    {
        DropBookmarks(first, last);
        DropFolds(first, last);
    }

    /// <summary>Moves bookmarks and fold starts with block [s, e] together.</summary>
    public void MoveMarks(int s, int e, int dir)
    {
        MoveBookmarks(s, e, dir);
        MoveFolds(s, e, dir);
    }

    /// <summary>Clamps bookmarks and fold starts to the document together.</summary>
    public void ClampMarks(int count)
    {
        ClampBookmarks(count);
        ClampFolds(count);
    }

    /// <summary>Shifts fold starts (ends are recalculated from indentation).</summary>
    public void ShiftFolds(int fromRow, int delta) => ShiftSet(Folds, fromRow, delta);

    /// <summary>Removes folds in the [first, last] range.</summary>
    public void DropFolds(int first, int last) => DropSet(Folds, first, last);

    /// <summary>Moves fold starts along with the moved block.</summary>
    public void MoveFolds(int s, int e, int dir) => MoveSet(Folds, s, e, dir);

    /// <summary>Discards folds outside the document.</summary>
    public void ClampFolds(int count) => Folds.RemoveWhere(r => r < 0 || r >= count);

    private static void ShiftSet(SortedSet<int> set, int fromRow, int delta)
    {
        if (delta == 0)
            return;
        var moved = set.GetViewBetween(fromRow, int.MaxValue).ToList();
        foreach (int r in moved)
        {
            set.Remove(r);
            if (r + delta >= 0)
                set.Add(r + delta);
        }
    }

    private static void DropSet(SortedSet<int> set, int first, int last)
    {
        if (first > last)
            return;
        set.RemoveWhere(r => r >= first && r <= last);
    }

    private static void MoveSet(SortedSet<int> set, int s, int e, int dir)
    {
        var moved = set.GetViewBetween(s, e).ToList();
        if (dir < 0 && set.Contains(s - 1))
        {
            set.Remove(s - 1);
            set.Add(e);
        }
        if (dir > 0 && set.Contains(e + 1))
        {
            set.Remove(e + 1);
            set.Add(s);
        }
        foreach (int r in moved)
        {
            set.Remove(r);
            set.Add(r + dir);
        }
    }

    /// <summary>Gets the tab title: file name or untitled, with "*" when dirty.</summary>
    public string TabTitle(Loc loc) =>
        (Buf.FilePath is null ? loc["status.untitled"] : Path.GetFileName(Buf.FilePath))
        + (Buf.IsModified ? "*" : string.Empty);
}
