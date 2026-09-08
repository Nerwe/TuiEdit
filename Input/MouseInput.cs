namespace TuiEdit;

/// <summary>Действие мыши (SGR 1006 + conhost API).</summary>
internal enum MouseAction
{
    LeftPress,
    WheelUp,
    WheelDown,
    /// <summary>Движение (hover/drag): только позиция, без действий.</summary>
    Move,
}

/// <summary>Событие мыши; координаты 0-based (терминал шлёт 1-based).</summary>
internal sealed record MouseInput(int X, int Y, MouseAction Action) : InputEvent
{
    /// <summary>
    /// Разобрать хвост пачки после ESC («[&lt;Cb;Cx;CyM/m»).
    /// Release (m), средняя/правая кнопки и мусор — null (игнор).
    /// </summary>
    public static MouseInput? TryParse(string burst)
    {
        if (burst.Length < 6 || burst[0] != '[' || burst[1] != '<')
            return null;
        char kind = burst[^1];
        if (kind != 'M' && kind != 'm')
            return null;
        string[] parts = burst[2..^1].Split(';');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int cb)
            || !int.TryParse(parts[1], out int cx)
            || !int.TryParse(parts[2], out int cy))
            return null;
        if (kind == 'm')
            return null; // отпускание не отслеживаем
        int x = Math.Max(0, cx - 1), y = Math.Max(0, cy - 1);
        if ((cb & 64) != 0)
            return new MouseInput(x, y, (cb & 1) != 0 ? MouseAction.WheelDown : MouseAction.WheelUp);
        if ((cb & 32) != 0)
            return new MouseInput(x, y, MouseAction.Move); // движение (?1002)
        if ((cb & 3) != 0)
            return null; // средняя/правая мимо
        return new MouseInput(x, y, MouseAction.LeftPress);
    }
}
