namespace TuiEdit;

internal sealed class InputReader
{
    /// <summary>Mouse capture level (AppSettings.Mouse, off by default).</summary>
    public static MouseLevel MouseLevel { get; set; } = MouseLevel.Off;

    /// <summary>Mouse enabled (any level except Off).</summary>
    public static bool MouseEnabled => MouseLevel != MouseLevel.Off;

    /// <summary>Process-wide parser wired to the real console (tests build their own).</summary>
    internal static InputParser Parser { get; } =
        new(Terminal.IsKeyPending, () => ReadKeyWithRetry(() => Console.ReadKey(intercept: true)));

    /// <summary>Transient console reads retried before giving up.</summary>
    internal const int MaxConsoleReadRetries = 3;

    /// <summary>Backoff between console read retries (a dying sibling process can fail one read).</summary>
    internal const int ConsoleReadRetryMs = 50;

    /// <summary>
    /// Reads one key, retrying transient console failures with backoff: a dying
    /// sibling process on the same console can fail a single read (dotnet#88697),
    /// and a resize can surface ERROR_PIPE_NOT_CONNECTED — losing the session to
    /// the blip is worse than waiting it out. Permanent failures still throw.
    /// </summary>
    internal static ConsoleKeyInfo ReadKeyWithRetry(Func<ConsoleKeyInfo> read)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return read();
            }
            catch (InvalidOperationException) when (attempt < MaxConsoleReadRetries)
            {
                attempt++;
                try
                {
                    Thread.Sleep(ConsoleReadRetryMs);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Non-blocking poll for burst coalescing (held keys, paste floods):
    /// the next event when input is already pending, null otherwise. Never blocks.
    /// </summary>
    public static InputEvent? TryReadPending() => Parser.TryRead(MouseEnabled);

    /// <summary>
    /// Handles one frame's worth of input: invokes <paramref name="handle"/> for every
    /// pending event without blocking, so a flood costs mutations but a single frame.
    /// Returns the handled count. A throwing handler aborts the drain (the remainder
    /// stays queued for the next frame — natural backpressure).
    /// </summary>
    /// <param name="handle">Handles one input event (may throw).</param>
    internal static int DrainPending(Action<InputEvent> handle)
    {
        int n = 0;
        while (TryReadPending() is InputEvent ev)
        {
            handle(ev);
            n++;
        }
        return n;
    }

    public static InputEvent Read()
    {
        if (MouseEnabled && OperatingSystem.IsWindows())
        {
            // We drain the conhost queue ourselves: .NET ReadKey swallows mouse events,
            // and blocks waiting for keys only. We call ReadKey only when a
            // key-down is at the front — then it returns instantly, losing nothing.
            int silent = 0, motion = 0, junk = 0, takeNull = 0;
            while (true)
            {
                if (!Terminal.TryPeek(out Terminal.InputRecord rec))
                {
                    silent = 0; // queue drained — progress, not a flood
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
                        {
                            motion++;
                            if (Terminal.SilentPollExceeded(++silent))
                            {
                                InputLog.DrainFlood(silent,
                                    $"motion={motion} junk={junk} takenull={takeNull} {Terminal.DescribeHead()}");
                                break; // flood: fall through to the key-wait path
                            }
                            continue;
                        }
                        InputLog.Yield(mev); // conhost path bypasses the parser: log here
                        return mev;
                    }
                    takeNull++;
                    if (Terminal.SilentPollExceeded(++silent))
                    {
                        InputLog.DrainFlood(silent,
                            $"motion={motion} junk={junk} takenull={takeNull} {Terminal.DescribeHead()}");
                        break; // middle/right/wheel-0 storm: same recovery
                    }
                    continue;
                }
                if (rec.EventType != Terminal.KEY_EVENT || rec.KeyEvent.KeyDown == 0)
                {
                    int dropped = Terminal.DropJunkBatch(128);
                    if (dropped > 0)
                    {
                        junk += dropped;
                        silent += dropped;
                    }
                    else
                    {
                        Terminal.Take(); // key-up, resize, focus — skip (phantom-safe single)
                        junk++;
                        silent++;
                    }
                    if (Terminal.SilentPollExceeded(silent))
                    {
                        InputLog.DrainFlood(silent,
                            $"motion={motion} junk={junk} takenull={takeNull} {Terminal.DescribeHead()}");
                        break; // junk storm: same recovery
                    }
                    continue;
                }
                break; // key-down at the front
            }
        }
        // Deferred events are older than fresh console input, so the parser (FIFO) goes first.
        InputEvent? ev = Parser.TryRead(MouseEnabled);
        if (ev is not null)
            return ev;
        Parser.Feed(ReadKeyWithRetry(() => Console.ReadKey(intercept: true))); // intended blocking read
        // Non-null: at least one key is queued, so TryRead always produces an event.
        return Parser.TryRead(MouseEnabled)!;
    }
}
