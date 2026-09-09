namespace TuiEdit;

/// <summary>Состояние диалога настроек: выбранная строка (значения живут в <see cref="AppSettings"/>); строка 0 — тема, 1 — язык, 2 — номера строк, 3 — перенос строк, 4 — пробелы, 5 — линейка, 6 — копия .bak, 7 — направляющие, 8 — сессия, 9 — мышь.</summary>
public sealed class SettingsDialogState
{
    public const int RowCount = 12;

    public int Row { get; private set; }

    public void Move(int delta) => Row = Math.Clamp(Row + delta, 0, RowCount - 1);

    public void MoveTo(int row) => Row = Math.Clamp(row, 0, RowCount - 1);

    /// <summary>Листание значения по кругу.</summary>
    public static int Cycle(int index, int count, int dir) =>
        count <= 0 ? 0 : ((index + dir) % count + count) % count;
}
