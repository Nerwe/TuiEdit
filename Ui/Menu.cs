namespace TuiEdit;

/// <summary>Пункт выпадающего меню: подпись, хоткей (одиночная буква без Enter), текст глобального шортката справа и команда.</summary>
/// <remarks>Устройство повторяет MS Edit: пункт срабатывает по клику, хоткею
/// или глобальному шорткату (<c>draw_menubar.rs</c> в microsoft/edit).</remarks>
public sealed record MenuItem(string Label, char Hotkey, string? Shortcut, EditorCommand Command);

public sealed record TopMenu(string Label, char Hotkey, List<MenuItem> Items);

public sealed class MenuState
{
    /// <summary>Все меню бара слева направо.</summary>
    public List<TopMenu> Menus { get; }

    public int OpenIndex { get; private set; }

    public int SelectedIndex { get; private set; }

    public MenuState(List<TopMenu> menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        if (menus.Count == 0 || menus.Any(m => m.Items.Count == 0))
            throw new ArgumentException("Меню и пункты не должны быть пустыми.", nameof(menus));
        Menus = menus;
    }

    public TopMenu Current => Menus[OpenIndex];

    public MenuItem Selected => Current.Items[SelectedIndex];

    /// <summary>Раскрыть меню (выбор сбрасывается на первый пункт).</summary>
    public void Open(int index)
    {
        OpenIndex = Math.Clamp(index, 0, Menus.Count - 1);
        SelectedIndex = 0;
    }

    /// <summary>Выбор вверх с зацикливанием.</summary>
    public void MoveUp()
    {
        int n = Current.Items.Count;
        SelectedIndex = (SelectedIndex - 1 + n) % n;
    }

    /// <summary>Выбор вниз с зацикливанием.</summary>
    public void MoveDown() => SelectedIndex = (SelectedIndex + 1) % Current.Items.Count;

    /// <summary>Переход к соседнему меню влево (выбор сбрасывается).</summary>
    public void MoveLeft() => Open((OpenIndex - 1 + Menus.Count) % Menus.Count);

    /// <summary>Переход к соседнему меню вправо (выбор сбрасывается).</summary>
    public void MoveRight() => Open((OpenIndex + 1) % Menus.Count);

    /// <summary>Пункт раскрытого меню по хоткею (без учёта регистра) или null.</summary>
    public MenuItem? FindItemByHotkey(char ch)
    {
        char c = char.ToUpperInvariant(ch);
        return Current.Items.FirstOrDefault(i => char.ToUpperInvariant(i.Hotkey) == c);
    }

    /// <summary>Индекс меню бара по хоткею (без учёта регистра) или -1.</summary>
    public int FindMenuByHotkey(char ch)
    {
        char c = char.ToUpperInvariant(ch);
        for (int i = 0; i < Menus.Count; i++)
            if (char.ToUpperInvariant(Menus[i].Hotkey) == c)
                return i;
        return -1;
    }
}
