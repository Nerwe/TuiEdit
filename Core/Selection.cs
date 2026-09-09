namespace TuiEdit;

/// <summary>Represents a selection: anchor (fixed end) plus cursor (active end).</summary>
internal sealed class TextSelection
{
    public int AnchorRow { get; set; }

    public int AnchorCol { get; set; }

    public bool Active { get; private set; }

    /// <summary>Determines whether a non-collapsed selection exists relative to the cursor.</summary>
    public bool HasSelection(int curRow, int curCol) =>
        Active && (AnchorRow != curRow || AnchorCol != curCol);

    public void Start(int curRow, int curCol)
    {
        AnchorRow = curRow;
        AnchorCol = curCol;
        Active = true;
    }

    public void Clear() => Active = false;

    /// <summary>Normalizes bounds to [start, end): start &lt;= end lexicographically.</summary>
    public (int StartRow, int StartCol, int EndRow, int EndCol) Normalize(int curRow, int curCol) =>
        (AnchorRow, AnchorCol, curRow, curCol) switch
        {
            var (ar, ac, cr, cc) when ar < cr || (ar == cr && ac <= cc) => (ar, ac, cr, cc),
            var (ar, ac, cr, cc) => (cr, cc, ar, ac),
        };
}
