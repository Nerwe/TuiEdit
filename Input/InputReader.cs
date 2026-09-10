namespace TuiEdit;

internal sealed class InputReader
{
    /// <summary>Mouse capture level (AppSettings.Mouse, off by default).</summary>
    public static MouseLevel MouseLevel { get; set; } = MouseLevel.Off;

    /// <summary>Mouse enabled (any level except Off).</summary>
    public static bool MouseEnabled => MouseLevel != MouseLevel.Off;

    /// <summary>Process-wide parser wired to the real console (tests build their own).</summary>
    internal static InputParser Parser { get; } =
        new(Terminal.IsKeyPending, () => Console.ReadKey(intercept: true));

    public static InputEvent Read()
    {
        if (MouseEnabled && OperatingSystem.IsWindows())
        {
            // We drain the conhost queue ourselves: .NET ReadKey swallows mouse events,
            // and blocks waiting for keys only. We call ReadKey only when a
            // key-down is at the front — then it returns instantly, losing nothing.
            while (true)
            {
                if (!Terminal.TryPeek(out Terminal.InputRecord rec))
                {
                    if (!Terminal.WaitForInput(50))
                        break; // console error — legacy path
                    continue;
                }
                if (rec.EventType == Terminal.MOUSE_EVENT)
                {
                    if (Terminal.Take() is MouseInput mev)
                    {
                        // Backpressure: stale motion (something already behind it)
                        // is eaten without a render — otherwise the flood starves keys.
                        if (mev.Action == MouseAction.Move && Terminal.PendingCount() > 0)
                            continue;
                        return mev;
                    }
                    continue;
                }
                if (rec.EventType != Terminal.KEY_EVENT || rec.KeyEvent.KeyDown == 0)
                {
                    Terminal.Take(); // key-up, resize, focus — skip
                    continue;
                }
                break; // key-down at the front
            }
        }
        // Deferred events are older than fresh console input, so the parser (FIFO) goes first.
        InputEvent? ev = Parser.TryRead(MouseEnabled);
        if (ev is not null)
            return ev;
        Parser.Feed(Console.ReadKey(intercept: true)); // intended blocking read
        // Non-null: at least one key is queued, so TryRead always produces an event.
        return Parser.TryRead(MouseEnabled)!;
    }
}
