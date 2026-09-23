namespace TuiEdit;

/// <summary>
/// Extra carets for multi-cursor editing. The primary caret lives in
/// <see cref="DocTab"/> (<c>Row</c>/<c>Col</c>); this holds the rest.
/// Order is irrelevant; enumeration is normalized (row, col).
/// </summary>
internal sealed class MultiCaret
{
    private readonly List<(int Row, int Col)> _carets = new();

    /// <summary>Extra caret count (primary excluded).</summary>
    public int Count => _carets.Count;

    /// <summary>Whether multi-cursor mode is active.</summary>
    public bool HasMultiple => _carets.Count > 0;

    /// <summary>Carets in document order (row, then col).</summary>
    public IReadOnlyList<(int Row, int Col)> Ordered =>
        _carets.OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();

    /// <summary>
    /// Adds a caret. Duplicates (including the primary) are ignored.
    /// Returns <see langword="true"/> when the set changed.
    /// </summary>
    public bool Add(int row, int col, int primaryRow, int primaryCol)
    {
        if ((row == primaryRow && col == primaryCol) || _carets.Contains((row, col)))
            return false;
        _carets.Add((row, col));
        return true;
    }

    /// <summary>Removes all extra carets (back to single-caret mode).</summary>
    public void Clear() => _carets.Clear();

    /// <summary>Drops carets outside the document after edits/undo.</summary>
    public void Clamp(int lineCount, Func<int, int> lineLength)
    {
        var seen = new HashSet<(int Row, int Col)>();
        for (int i = _carets.Count - 1; i >= 0; i--)
        {
            int r = Math.Clamp(_carets[i].Row, 0, Math.Max(0, lineCount - 1));
            int c = Math.Clamp(_carets[i].Col, 0, lineLength(r));
            var pos = (r, c);
            if (seen.Add(pos))
                _carets[i] = pos;
            else
                _carets.RemoveAt(i);
        }
    }
}
