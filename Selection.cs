namespace TuiEdit;

/// <summary>
/// Выделение текста: якорь (неподвижный конец) + курсор (активный конец).
/// Чистая модель без консоли — покрывается unit-тестами.
/// </summary>
public sealed class TextSelection
{
    /// <summary>Строка якоря.</summary>
    public int AnchorRow { get; set; }

    /// <summary>Колонка якоря (в символах).</summary>
    public int AnchorCol { get; set; }

    /// <summary>Режим выделения включён (якорь поставлен).</summary>
    public bool Active { get; private set; }

    /// <summary>Есть ли невырожденное выделение относительно курсора.</summary>
    public bool HasSelection(int curRow, int curCol) =>
        Active && (AnchorRow != curRow || AnchorCol != curCol);

    /// <summary>Поставить якорь в текущую позицию курсора.</summary>
    public void Start(int curRow, int curCol)
    {
        AnchorRow = curRow;
        AnchorCol = curCol;
        Active = true;
    }

    /// <summary>Снять выделение.</summary>
    public void Clear() => Active = false;

    /// <summary>
    /// Нормализованные границы [start, end): start &lt;= end лексикографически.
    /// </summary>
    public (int StartRow, int StartCol, int EndRow, int EndCol) Normalize(int curRow, int curCol) =>
        (AnchorRow, AnchorCol, curRow, curCol) switch
        {
            var (ar, ac, cr, cc) when ar < cr || (ar == cr && ac <= cc) => (ar, ac, cr, cc),
            var (ar, ac, cr, cc) => (cr, cc, ar, ac),
        };
}
