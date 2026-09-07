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

    /// <summary>Заголовок вкладки: имя файла или «без имени», грязным — «*».</summary>
    public string TabTitle(Loc loc) =>
        (Buf.FilePath is null ? loc["status.untitled"] : Path.GetFileName(Buf.FilePath))
        + (Buf.IsModified ? "*" : string.Empty);
}
