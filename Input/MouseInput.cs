namespace TuiEdit;

/// <summary>Mouse action (SGR 1006 + conhost API).</summary>
internal enum MouseAction
{
    LeftPress,
    WheelUp,
    WheelDown,
    /// <summary>Motion (hover/drag): position only, no actions.</summary>
    Move,
    MiddlePress,
    RightPress,
}

/// <summary>Which button participates in the event (release/motion without a button — None).</summary>
internal enum MouseButton
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>Modifiers held during the event (SGR cb bits / ControlKeyState).</summary>
[Flags]
internal enum MouseModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
}

/// <summary>
/// Mouse capture level (which reports we request from the terminal).
/// Off — native terminal selection; Basic — clicks+wheel (?1000);
/// Drag — plus motion with a held button (?1002); Motion — all motion (?1003).
/// </summary>
public enum MouseLevel
{
    Off,
    Basic,
    Drag,
    Motion,
}

/// <summary>Mouse event; 0-based coordinates (the terminal sends 1-based).</summary>
internal sealed record MouseInput(
    int X,
    int Y,
    MouseAction Action,
    MouseButton Button = MouseButton.None,
    MouseModifiers Modifiers = MouseModifiers.None,
    int Count = 1) : InputEvent
{
    /// <summary>
    /// Parse the burst tail after ESC ("[&lt;Cb;Cx;CyM/m").
    /// Buttons and modifiers are kept in the event; garbage — null (ignore).
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
        int x = Math.Max(0, cx - 1), y = Math.Max(0, cy - 1);
        MouseModifiers mods = ModifiersOf(cb);
        if (kind == 'm')
            return new MouseInput(x, y, MouseAction.Move); // release: hover position only
        if ((cb & 64) != 0)
            return new MouseInput(x, y, (cb & 1) != 0 ? MouseAction.WheelDown : MouseAction.WheelUp,
                MouseButton.None, mods);
        if ((cb & 32) != 0)
            return new MouseInput(x, y, MouseAction.Move, ButtonOf(cb & 3), mods); // motion (?1002)
        return (cb & 3) switch
        {
            0 => new MouseInput(x, y, MouseAction.LeftPress, MouseButton.Left, mods),
            1 => new MouseInput(x, y, MouseAction.MiddlePress, MouseButton.Middle, mods),
            2 => new MouseInput(x, y, MouseAction.RightPress, MouseButton.Right, mods),
            _ => new MouseInput(x, y, MouseAction.Move), // cb=3 without motion — release
        };
    }

    /// <summary>Modifiers from the SGR button code (bits 2/3/4).</summary>
    internal static MouseModifiers ModifiersOf(int cb)
    {
        MouseModifiers mods = MouseModifiers.None;
        if ((cb & 4) != 0)
            mods |= MouseModifiers.Shift;
        if ((cb & 8) != 0)
            mods |= MouseModifiers.Alt;
        if ((cb & 16) != 0)
            mods |= MouseModifiers.Ctrl;
        return mods;
    }

    private static MouseButton ButtonOf(int b) => b switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        _ => MouseButton.None,
    };
}
