namespace TuiEdit;

/// <summary>Состояние диалога настроек: выбранная строка (значения живут в <see cref="AppSettings"/>); строка 0 — тема, 1 — язык, 2 — номера строк, 3 — перенос строк, 4 — копия .bak, 5 — направляющие, 6 — сессия.</summary>
public sealed class SettingsDialogState
{
    public const int RowCount = 7;

    public int Row { get; private set; }

    public void Move(int delta) => Row = Math.Clamp(Row + delta, 0, RowCount - 1);

    /// <summary>Листание значения по кругу.</summary>
    public static int Cycle(int index, int count, int dir) =>
        count <= 0 ? 0 : ((index + dir) % count + count) % count;
}
