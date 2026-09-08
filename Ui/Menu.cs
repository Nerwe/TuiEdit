namespace TuiEdit;

/// <summary>Пункт выпадающего меню: подпись, хоткей (одиночная буква без Enter), текст глобального шортката справа и команда.</summary>
/// <remarks>Устройство повторяет MS Edit: пункт срабатывает по клику, хоткею
/// или глобальному шорткату (<c>draw_menubar.rs</c> в microsoft/edit).</remarks>
public sealed record MenuItem(string Label, char Hotkey, string? Shortcut, EditorCommand Command)
{
    public static MenuItem Separator => new(string.Empty, '\0', null, EditorCommand.None);

    public bool IsSeparator => Label.Length == 0;
}

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

    /// <summary>Раскрыть меню (выбор — на первый невырожденный пункт).</summary>
    public void Open(int index)
    {
        OpenIndex = Math.Clamp(index, 0, Menus.Count - 1);
        SelectedIndex = 0;
        if (Current.Items[0].IsSeparator)
            MoveDown();
    }

    /// <summary>Выбор вверх с зацикливанием (разделители пропускаем).</summary>
    public void MoveUp()
    {
        int n = Current.Items.Count;
        for (int i = 0; i < n; i++)
        {
            SelectedIndex = (SelectedIndex - 1 + n) % n;
            if (!Current.Items[SelectedIndex].IsSeparator)
                return;
        }
    }

    /// <summary>Выбор вниз с зацикливанием (разделители пропускаем).</summary>
    public void MoveDown()
    {
        int n = Current.Items.Count;
        for (int i = 0; i < n; i++)
        {
            SelectedIndex = (SelectedIndex + 1) % n;
            if (!Current.Items[SelectedIndex].IsSeparator)
                return;
        }
    }

    /// <summary>Переход к соседнему меню влево (выбор сбрасывается).</summary>
    public void MoveLeft() => Open((OpenIndex - 1 + Menus.Count) % Menus.Count);

    /// <summary>Переход к соседнему меню вправо (выбор сбрасывается).</summary>
    public void MoveRight() => Open((OpenIndex + 1) % Menus.Count);

    /// <summary>Пункт раскрытого меню по хоткею (без учёта регистра) или null.</summary>
    public MenuItem? FindItemByHotkey(char ch)
    {
        char c = char.ToUpperInvariant(ch);
        return Current.Items.FirstOrDefault(i => !i.IsSeparator && char.ToUpperInvariant(i.Hotkey) == c);
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

/// <summary>Хит-тесты мыши по меню (чистые; зеркало DrawMenuBar/DrawDropdown).</summary>
internal static class MenuHit
{
    /// <summary>Ячейка меню-бара (row 0) или null (мимо и за краем).</summary>
    public static int? BarHit(IReadOnlyList<TopMenu> menus, int x, int screenW)
    {
        int cx = 0;
        for (int i = 0; i < menus.Count; i++)
        {
            int cw = menus[i].Label.Length + 2; // " Label " как в DrawMenuBar
            if (cx + cw > screenW)
                break;
            if (x >= cx && x < cx + cw)
                return i;
            cx += cw;
        }
        return null;
    }

    /// <summary>Индекс пункта дропдауна или null (мимо, разделитель, не влез).</summary>
    public static int? DropdownHit(TopMenu m, int menuX, int x, int y, int w, int h)
    {
        int inner = 0;
        foreach (MenuItem it in m.Items)
        {
            string rc = it.Shortcut ?? it.Hotkey.ToString();
            inner = Math.Max(inner, 1 + it.Label.Length + 2 + rc.Length + 1);
        }
        int boxW = Math.Min(inner + 2, w - menuX);
        if (boxW < 10 || menuX >= w)
            return null;
        const int dy = 1;
        int maxRows = h - 1 - dy;
        if (maxRows < 3)
            return null;
        int rows = Math.Min(m.Items.Count, maxRows - 2);
        int row = y - (dy + 1);
        if (row < 0 || row >= rows || x < menuX || x >= menuX + boxW)
            return null;
        return m.Items[row].IsSeparator ? null : row;
    }
}
