namespace TuiEdit;

/// <summary>
/// Состояние диалога настроек: выбранная строка (значения живут в <see cref="AppSettings"/>).
/// Строка 0 — тема, 1 — язык, 2 — регистр поиска, 3 — целые слова.
/// </summary>
public sealed class SettingsDialogState
{
    /// <summary>Число строк.</summary>
    public const int RowCount = 4;

    /// <summary>Выбранная строка.</summary>
    public int Row { get; private set; }

    /// <summary>Движение по строкам.</summary>
    public void Move(int delta) => Row = Math.Clamp(Row + delta, 0, RowCount - 1);

    /// <summary>Листание значения по кругу.</summary>
    public static int Cycle(int index, int count, int dir) =>
        count <= 0 ? 0 : ((index + dir) % count + count) % count;
}
