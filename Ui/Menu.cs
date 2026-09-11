namespace TuiEdit;

/// <summary>Represents a dropdown menu item: label, hotkey (single letter without Enter), global shortcut text on the right, and command.</summary>
/// <remarks>Activates on click, hotkey, or global shortcut.</remarks>
internal sealed record MenuItem(string Label, char Hotkey, string? Shortcut, EditorCommand Command)
{
    public static MenuItem Separator => new(string.Empty, '\0', null, EditorCommand.None);

    public bool IsSeparator => Label.Length == 0;
}

internal sealed record TopMenu(string Label, char Hotkey, List<MenuItem> Items);

internal sealed class MenuState
{
    /// <summary>Gets all bar menus left to right.</summary>
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

    /// <summary>Opens the menu (selects the first non-degenerate item).</summary>
    public void Open(int index)
    {
        OpenIndex = Math.Clamp(index, 0, Menus.Count - 1);
        SelectedIndex = 0;
        if (Current.Items[0].IsSeparator)
            MoveDown();
    }

    /// <summary>Moves the selection up with wraparound (skips separators).</summary>
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

    /// <summary>Moves the selection down with wraparound (skips separators).</summary>
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

    /// <summary>Moves to the neighboring menu on the left (resets the selection).</summary>
    public void MoveLeft() => Open((OpenIndex - 1 + Menus.Count) % Menus.Count);

    /// <summary>Moves to the neighboring menu on the right (resets the selection).</summary>
    public void MoveRight() => Open((OpenIndex + 1) % Menus.Count);

    /// <summary>Finds the open-menu item by hotkey (case-insensitive) or returns null.</summary>
    public MenuItem? FindItemByHotkey(char ch)
    {
        char c = char.ToUpperInvariant(ch);
        return Current.Items.FirstOrDefault(i => !i.IsSeparator && char.ToUpperInvariant(i.Hotkey) == c);
    }

    /// <summary>Finds the bar menu index by hotkey (case-insensitive), or -1.</summary>
    public int FindMenuByHotkey(char ch)
    {
        char c = char.ToUpperInvariant(ch);
        for (int i = 0; i < Menus.Count; i++)
            if (char.ToUpperInvariant(Menus[i].Hotkey) == c)
                return i;
        return -1;
    }
}

/// <summary>Provides pure menu mouse hit-tests (mirrors DrawMenuBar/DrawDropdown).</summary>
internal static class MenuHit
{
    /// <summary>Hits a menu-bar cell (row 0) or returns null (miss past the edge).</summary>
    public static int? BarHit(IReadOnlyList<TopMenu> menus, int x, int screenW)
    {
        int cx = 0;
        for (int i = 0; i < menus.Count; i++)
        {
            int cw = menus[i].Label.Length + 2; // " Label " as in DrawMenuBar
            if (cx + cw > screenW)
                break;
            if (x >= cx && x < cx + cw)
                return i;
            cx += cw;
        }
        return null;
    }

    /// <summary>Computes the dropdown x from the open menu index (mirrors DrawDropdown).</summary>
    public static int MenuX(MenuState menu)
    {
        int x = 0;
        for (int i = 0; i < menu.OpenIndex && i < menu.Menus.Count; i++)
            x += menu.Menus[i].Label.Length + 2;
        return x;
    }

    /// <summary>Hits the dropdown row under coordinates or returns null (miss or overflow).</summary>
    private static int? DropdownRow(TopMenu m, int menuX, int x, int y, int w, int h)
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
        return row;
    }

    /// <summary>Hits a dropdown item index or returns null (miss, separator, or overflow).</summary>
    public static int? DropdownHit(TopMenu m, int menuX, int x, int y, int w, int h) =>
        DropdownRow(m, menuX, x, y, w, h) switch
        {
            int r when !m.Items[r].IsSeparator => r,
            _ => null,
        };

    /// <summary>Whether coordinates land on a separator row inside the dropdown box.</summary>
    public static bool IsSeparatorHit(TopMenu m, int menuX, int x, int y, int w, int h) =>
        DropdownRow(m, menuX, x, y, w, h) is int r && m.Items[r].IsSeparator;
}
