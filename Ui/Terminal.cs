using System.Runtime.InteropServices;

namespace TuiEdit;

/// <summary>Controls terminal modes: enables VT sequences (needed for truecolor colors and bracketed paste).</summary>
internal static class Terminal
{
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    internal const ushort KEY_EVENT = 0x0001;
    internal const ushort MOUSE_EVENT = 0x0002;

    private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    private const uint MOUSE_MOVED = 0x0001;
    private const uint MOUSE_WHEELED = 0x0004;

    private const uint SHIFT_PRESSED = 0x0010;
    private const uint ALT_PRESSED = 0x0001 | 0x0002; // left|right Alt
    private const uint CTRL_PRESSED = 0x0004 | 0x0008; // left|right Ctrl

    private const uint WAIT_FAILED = 0xFFFFFFFF;

    private static uint _stdinOldMode;
    private static bool _stdinModeSaved;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyEventRecord
    {
        public int KeyDown; // BOOL
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public char UnicodeChar;
        public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseEventRecord
    {
        public Coord MousePosition;
        public uint ButtonState;
        public uint ControlKeyState;
        public uint EventFlags;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord KeyEvent;
        [FieldOffset(4)] public MouseEventRecord MouseEvent;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekConsoleInput(IntPtr hConsoleInput,
        [In, Out] InputRecord[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(IntPtr hConsoleInput,
        [In, Out] InputRecord[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumberOfConsoleInputEvents(IntPtr hConsoleInput, out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushConsoleInputBuffer(IntPtr hConsoleInput);

    private const int TCIFLUSH = 0;

    [DllImport("libc")]
    private static extern int tcflush(int fd, int queue);

    /// <summary>Enables VT sequence processing for output; on Unix VT exists from the start (except dumb terminals).</summary>
    public static bool TryEnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable("TERM") switch
            {
                null or "" or "dumb" or "unknown" => false,
                _ => true,
            };
        }
        try
        {
            IntPtr h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return false;
            if (!GetConsoleMode(h, out uint mode))
                return false;
            return SetConsoleMode(h, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Dims input processing (without VT_INPUT): clears LINE/ECHO/PROCESSED, otherwise conhost intercepts Ctrl+S as output pause (XOFF); deliberately omits VIRTUAL_TERMINAL_INPUT - with it arrows/F-keys/Alt combos arrive as ESC sequences that .NET ReadKey never collects; skips WINDOW_INPUT (resize is visible via WindowWidth/Height); does nothing on Unix.</summary>
    public static bool TryEnableRawInput()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return false;
            if (!GetConsoleMode(h, out uint mode))
                return false;
            _stdinOldMode = mode;
            _stdinModeSaved = true;
            uint raw = mode & ~(ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT);
            return SetConsoleMode(h, raw);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the SGR mouse-enable sequence per level (a pure function for tests).
    /// Disables higher modes first: some terminals treat ?1000/?1002/?1003
    /// as one family where the last sequence wins.
    /// </summary>
    internal static string MouseEnableSequence(MouseLevel level) => level switch
    {
        MouseLevel.Basic => "\x1b[?1002l\x1b[?1003l\x1b[?1000h\x1b[?1006h",
        MouseLevel.Drag => "\x1b[?1003l\x1b[?1000h\x1b[?1002h\x1b[?1006h",
        MouseLevel.Motion => "\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h",
        _ => "",
    };

    /// <summary>Builds the sequence that disables all mouse modes at once.</summary>
    internal static string MouseDisableSequence() => "\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l";

    /// <summary>Enables mouse reports (clicks plus wheel) and the SGR extension; terminals without support ignore them.</summary>
    public static void TryEnableMouse() => SetMouseLevel(MouseLevel.Basic);

    /// <summary>Enables mouse reports at the given level.</summary>
    public static void SetMouseLevel(MouseLevel level)
    {
        try
        {
            if (level == MouseLevel.Off)
                DisableMouse();
            else
                Console.Write(MouseEnableSequence(level));
        }
        catch
        {
        }
    }

    /// <summary>Disables mouse reports (calls on exit and in the crash handler).</summary>
    public static void DisableMouse()
    {
        try { Console.Write(MouseDisableSequence()); } catch { }
    }

    /// <summary>Enables window focus reports (?1004: ESC[I / ESC[O).</summary>
    public static void TryEnableFocusTracking()
    {
        try { Console.Write("\x1b[?1004h"); } catch { }
    }

    /// <summary>Disables focus reports (exit, crash handler).</summary>
    public static void DisableFocusTracking()
    {
        try { Console.Write("\x1b[?1004l"); } catch { }
    }

    /// <summary>
    /// Resends active modes: Windows Terminal/ConPTY silently resets
    /// DEC modes on focus loss. Calls on focus-in (ESC[I).
    /// </summary>
    public static void RestoreModes(MouseLevel level)
    {
        if (level == MouseLevel.Off)
            return;
        try
        {
            Console.Write(MouseEnableSequence(level));
            Console.Write("\x1b[?1004h"); // Focus tracking could be reset too
            Console.Write("\x1b[?2004h"); // Bracketed paste - same
        }
        catch
        {
        }
    }

    /// <summary>
    /// Peeks the first input queue record without consuming it (Windows only).
    /// A successfully peeked record counts as present even with an unknown
    /// (zero) type: conhost does emit those, and treating present-as-absent
    /// spins the read loop forever on a signaled handle (100% CPU, no progress,
    /// no logs). Both consumers skip non-key/non-mouse records via Take().
    /// </summary>
    internal static bool TryPeek(out InputRecord rec)
    {
        rec = default;
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return false;
            var buf = new InputRecord[1];
            if (!PeekConsoleInput(h, buf, 1, out uint n) || n != 1)
                return false;
            rec = buf[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Consecutive <see cref="Take"/> read failures that prove an unconsumable
    /// record (Peek reports it, Read cannot take it — a zero-type ConPTY slot),
    /// then the queue is flushed once. Legit records of any type always read,
    /// so a streak this long means the head is pathological, not busy.
    /// </summary>
    internal const int TakeFailFlushThreshold = 1000;
    private static int _takeFailStreak;

    /// <summary>Pure trip test for the stuck-record breaker (unit-testable).</summary>
    internal static bool TakeFailStreakTripsFlush(int streak) => streak >= TakeFailFlushThreshold;

    /// <summary>
    /// Silent queue polls after which a peek/take loop stops and recovers:
    /// polls that neither produce a key nor drain the queue mean the head
    /// record is unconsumable (or an endless supply) — polling on burns CPU
    /// with zero logs and starves the main loop.
    /// </summary>
    internal const int MaxSilentPoll = 4096;

    /// <summary>Pure trip test for the silent-poll budget (unit-testable).</summary>
    internal static bool SilentPollExceeded(int silent) => silent >= MaxSilentPoll;

    /// <summary>
    /// One-shot peek describing the queue head for diagnostics
    /// (type/keydown/char — the mouse view overlaps the same bytes).
    /// Never throws, returns "head=empty" when nothing is queued.
    /// </summary>
    internal static string DescribeHead()
    {
        try
        {
            if (TryPeek(out InputRecord rec))
                return $"head={rec.EventType}/{rec.KeyEvent.KeyDown}/U+{(int)rec.KeyEvent.UnicodeChar:X4}";
            return "head=empty";
        }
        catch
        {
            return "head=error";
        }
    }

    /// <summary>
    /// Byte-exact record comparison: the 16-byte Event union is fully covered
    /// by the key-event view (4+2+2+2+2+4), which overlaps the mouse view.
    /// </summary>
    internal static bool SameRecord(InputRecord a, InputRecord b) =>
        a.EventType == b.EventType
        && a.KeyEvent.KeyDown == b.KeyEvent.KeyDown
        && a.KeyEvent.RepeatCount == b.KeyEvent.RepeatCount
        && a.KeyEvent.VirtualKeyCode == b.KeyEvent.VirtualKeyCode
        && a.KeyEvent.VirtualScanCode == b.KeyEvent.VirtualScanCode
        && a.KeyEvent.UnicodeChar == b.KeyEvent.UnicodeChar
        && a.KeyEvent.ControlKeyState == b.KeyEvent.ControlKeyState;

    private static void NoteTakeFailure()
    {
        if (TakeFailStreakTripsFlush(++_takeFailStreak))
        {
            _takeFailStreak = 0;
            InputLog.StuckFlush(TakeFailFlushThreshold);
            RecoverStuckInput();
        }
    }

    /// <summary>
    /// Nukes an unconsumable queue head from outside the read path
    /// (also drops typeahead accumulated while stuck — better than a hang).
    /// Best-effort, never throws.
    /// </summary>
    internal static void RecoverStuckInput()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return;
            FlushConsoleInputBuffer(h);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Consumes one record from the front; translates a mouse event (returns null for the rest).
    /// Verifies the head actually advanced: a successful Read does not always dequeue
    /// (phantom zero-type ConPTY slot) — without the check both peek/take loops spin
    /// forever with zero failures counted and zero logs.
    /// </summary>
    internal static MouseInput? Take()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return null;
            var buf = new InputRecord[1];
            if (!ReadConsoleInput(h, buf, 1, out uint n) || n != 1)
            {
                NoteTakeFailure();
                return null;
            }
            if (TryPeek(out InputRecord head) && SameRecord(head, buf[0]))
            {
                NoteTakeFailure(); // phantom: Read "succeeded" but the head never moved
                return null;
            }
            _takeFailStreak = 0; // reset only on verified consumption, so phantoms accumulate
            return buf[0].EventType == MOUSE_EVENT ? TranslateMouse(buf[0].MouseEvent) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Peeks whether a key-down is ahead. Ignores mouse records, consumes key-up and other junk
    /// (.NET never reports key releases anyway). Serves readiness checks:
    /// bare KeyAvailable is true on mouse too, while ReadKey over it
    /// blocks/swallows input.
    /// </summary>
    internal static bool IsKeyPending()
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch
            {
                return false;
            }
        }
        try
        {
            int silent = 0;
            while (TryPeek(out InputRecord rec))
            {
                if (rec.EventType == MOUSE_EVENT)
                    return false;
                if (rec.EventType == KEY_EVENT && rec.KeyEvent.KeyDown != 0)
                    return true;
                Take(); // Consumes key-up, resize, focus and looks further
                if (SilentPollExceeded(++silent))
                {
                    InputLog.DrainFlood(silent, $"pending {DescribeHead()}");
                    return false; // phantom head: Take already flushed+logged; stop polling
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Gets how many events are queued (backpressure for motion).</summary>
    internal static uint PendingCount()
    {
        if (!OperatingSystem.IsWindows())
            return 0;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return 0;
            return GetNumberOfConsoleInputEvents(h, out uint n) ? n : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Sleeps until input (avoids spinning the CPU while polling the queue).</summary>
    internal static bool WaitForInput(int milliseconds)
    {
        if (!OperatingSystem.IsWindows())
            return true;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return false;
            return WaitForSingleObject(h, (uint)Math.Max(0, milliseconds)) != WAIT_FAILED;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Translates MOUSE_EVENT_RECORD - a v1 event (click/wheel, coordinates already 0-based).</summary>
    internal static MouseInput? TranslateMouse(MouseEventRecord r)
    {
        int x = Math.Max(0, (int)r.MousePosition.X);
        int y = Math.Max(0, (int)r.MousePosition.Y);
        MouseModifiers mods = MouseModifiers.None;
        if ((r.ControlKeyState & SHIFT_PRESSED) != 0)
            mods |= MouseModifiers.Shift;
        if ((r.ControlKeyState & ALT_PRESSED) != 0)
            mods |= MouseModifiers.Alt;
        if ((r.ControlKeyState & CTRL_PRESSED) != 0)
            mods |= MouseModifiers.Ctrl;
        if ((r.EventFlags & MOUSE_WHEELED) != 0)
        {
            short delta = (short)((r.ButtonState >> 16) & 0xFFFF);
            if (delta == 0)
                return null;
            return new MouseInput(x, y, delta > 0 ? MouseAction.WheelUp : MouseAction.WheelDown,
                MouseButton.None, mods);
        }
        if ((r.EventFlags & MOUSE_MOVED) != 0)
            return new MouseInput(x, y, MouseAction.Move, PressedButton(r.ButtonState), mods); // Motion: hover
        if ((r.ButtonState & FROM_LEFT_1ST_BUTTON_PRESSED) == 0)
            return r.ButtonState == 0 && r.EventFlags == 0
                ? new MouseInput(x, y, MouseAction.Move) // Release: hover position only
                : null; // Middle/right
        return new MouseInput(x, y, MouseAction.LeftPress, MouseButton.Left, mods);
    }

    private static MouseButton PressedButton(uint buttons) =>
        (buttons & FROM_LEFT_1ST_BUTTON_PRESSED) != 0 ? MouseButton.Left : MouseButton.None;

    /// <summary>
    /// Toggles mouse via conhost-API (.NET ReadKey never reports it). Disabling also restores
    /// QuickEdit as it was (RestoreInput on exit restores everything).
    /// </summary>
    public static void ApplyMouseInput(bool on)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!_stdinModeSaved)
            return; // Console is not ours (tests, redirect) - leaves modes alone
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return;
            if (!GetConsoleMode(h, out uint mode))
                return;
            uint next;
            if (on)
            {
                // Disables QuickEdit, otherwise clicks go to the conhost selection.
                // EXTENDED_FLAGS is required to change QuickEdit.
                next = (mode & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS | ENABLE_MOUSE_INPUT;
            }
            else
            {
                next = mode & ~ENABLE_MOUSE_INPUT;
                next = ((_stdinOldMode & ENABLE_QUICK_EDIT_MODE) != 0
                    ? next | ENABLE_QUICK_EDIT_MODE
                    : next & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS;
            }
            SetConsoleMode(h, next);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Discards queued console input: typeahead and flood leftovers (e.g. held-key
    /// repeats still arriving at quit) must not leak into the shell after exit.
    /// Best-effort, never throws.
    /// </summary>
    public static void DiscardPendingInput()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                    return;
                FlushConsoleInputBuffer(h);
            }
            else
            {
                _ = tcflush(0, TCIFLUSH);
            }
        }
        catch
        {
        }
    }

    /// <summary>Restores the console input mode (calls on exit).</summary>
    public static void RestoreInput()
    {
        if (!_stdinModeSaved)
            return;
        _stdinModeSaved = false;
        try
        {
            IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return;
            SetConsoleMode(h, _stdinOldMode);
        }
        catch
        {
        }
    }
}
