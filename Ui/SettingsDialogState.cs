namespace TuiEdit;

/// <summary>Holds settings dialog state: the selected row (values live in <see cref="AppSettings"/>); row 0 is theme, 1 language, 2 line numbers, 3 word wrap, 4 whitespace, 5 ruler, 6 .bak backup, 7 guides, 8 session, 9 mouse.</summary>
public sealed class SettingsDialogState
{
    public const int RowCount = 13;

    public int Row { get; private set; }

    public void Move(int delta) => Row = Math.Clamp(Row + delta, 0, RowCount - 1);

    public void MoveTo(int row) => Row = Math.Clamp(row, 0, RowCount - 1);

    /// <summary>Cycles a value around.</summary>
    public static int Cycle(int index, int count, int dir) =>
        count <= 0 ? 0 : ((index + dir) % count + count) % count;
}
