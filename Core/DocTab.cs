namespace TuiEdit;

/// <summary>
/// Вкладка документа: буфер + состояние вида (курсор, скролл, выделение).
/// Активная вкладка держит то же состояние в полях редактора (кэш);
/// при переключении состояние сохраняется/считывается (см. TuiEditor).
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

    /// <summary>Подсветка синтаксиса вкладки (кэш инвалидируется по версии буфера).</summary>
    public SyntaxHighlighter Highlighter { get; } = new();

    /// <summary>Закладки: строки (0-based). Сдвиг — через Shift/Drop/Move при правках.</summary>
    public SortedSet<int> Bookmarks { get; } = new();

    /// <summary>Свёртки: стартовые строки блоков. Концы считаются по отступу.</summary>
    public SortedSet<int> Folds { get; } = new();

    /// <summary>Переключить закладку на строке.</summary>
    public bool ToggleBookmark(int row)
    {
        if (!Bookmarks.Add(row))
        {
            Bookmarks.Remove(row);
            return false;
        }
        return true;
    }

    /// <summary>Сдвинуть закладки начиная со строки (для вставки/удаления строк).</summary>
    public void ShiftBookmarks(int fromRow, int delta) => ShiftSet(Bookmarks, fromRow, delta);

    /// <summary>Убрать закладки в диапазоне [first, last] (удаляемые строки).</summary>
    public void DropBookmarks(int first, int last) => DropSet(Bookmarks, first, last);

    /// <summary>Закладки вслед за двигаемым блоком [s, e] (dir −1 вверх, +1 вниз).</summary>
    public void MoveBookmarks(int s, int e, int dir) => MoveSet(Bookmarks, s, e, dir);

    /// <summary>Выкинуть закладки за пределами документа (после undo и т.п.).</summary>
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

    /// <summary>Сдвинуть старты свёрток (концы пересчитываются по отступу).</summary>
    public void ShiftFolds(int fromRow, int delta) => ShiftSet(Folds, fromRow, delta);

    /// <summary>Убрать свёртки в диапазоне [first, last].</summary>
    public void DropFolds(int first, int last) => DropSet(Folds, first, last);

    /// <summary>Старты свёрток вслед за двигаемым блоком.</summary>
    public void MoveFolds(int s, int e, int dir) => MoveSet(Folds, s, e, dir);

    /// <summary>Выкинуть свёртки за пределами документа.</summary>
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

    /// <summary>Заголовок вкладки: имя файла или «без имени», грязным — «*».</summary>
    public string TabTitle(Loc loc) =>
        (Buf.FilePath is null ? loc["status.untitled"] : Path.GetFileName(Buf.FilePath))
        + (Buf.IsModified ? "*" : string.Empty);
}
