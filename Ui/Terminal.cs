using System.Runtime.InteropServices;

namespace TuiEdit;

/// <summary>Режимы терминала: включение VT-последовательностей (нужны truecolor-цветам и bracketed paste).</summary>
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

    /// <summary>Включить обработку VT-последовательностей для вывода; на Unix VT есть изначально (кроме dumb-терминалов).</summary>
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

    /// <summary>Пригасить обработку ввода (как MS Edit, но без VT_INPUT): снимаем LINE/ECHO/PROCESSED, иначе conhost перехватывает Ctrl+S как паузу вывода (XOFF); VIRTUAL_TERMINAL_INPUT намеренно не ставим — с ним стрелки/F-клавиши/Alt-комбинации приходят ESC-последовательностями, а .NET ReadKey их не собирает; WINDOW_INPUT не нужен (ресайз виден по WindowWidth/Height); на Unix ничего не делаем.</summary>
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

    /// <summary>Включить отчёты мыши (клики+колесо) и SGR-расширение; терминалы без поддержки игнорят.</summary>
    public static void TryEnableMouse()
    {
        try { Console.Write("\x1b[?1000h\x1b[?1006h"); } catch { }
    }

    /// <summary>Выключить отчёты мыши (вызывать при выходе и в crash handler).</summary>
    public static void DisableMouse()
    {
        try { Console.Write("\x1b[?1006l\x1b[?1000l"); } catch { }
    }

    /// <summary>
    /// Подсмотреть первую запись очереди ввода, не съедая (только Windows).
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
            return PeekConsoleInput(h, buf, 1, out uint n) && n == 1 && (rec = buf[0]).EventType != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Съесть одну запись спереди; событие мыши — транслировать (остальное — null).</summary>
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
                return null;
            return buf[0].EventType == MOUSE_EVENT ? TranslateMouse(buf[0].MouseEvent) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Впереди key-down? Мышиные записи игнорятся, key-up и прочий мусор
    /// съедаются (релизы клавиш .NET всё равно не отдаёт). Нужно проверкам
    /// готовности: голый KeyAvailable истинен и на мыши, а ReadKey поверх
    /// неё блокируется/глотает ввод.
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
            while (TryPeek(out InputRecord rec))
            {
                if (rec.EventType == MOUSE_EVENT)
                    return false;
                if (rec.EventType == KEY_EVENT && rec.KeyEvent.KeyDown != 0)
                    return true;
                Take(); // key-up, ресайз, фокус — съесть и смотреть дальше
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Спать до ввода (чтобы не крутить CPU в опросе очереди).</summary>
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

    /// <summary>MOUSE_EVENT_RECORD — событие v1 (клик/колесо, координаты уже 0-based).</summary>
    internal static MouseInput? TranslateMouse(MouseEventRecord r)
    {
        if ((r.EventFlags & MOUSE_WHEELED) != 0)
        {
            short delta = (short)((r.ButtonState >> 16) & 0xFFFF);
            if (delta == 0)
                return null;
            int x = Math.Max(0, (int)r.MousePosition.X);
            int y = Math.Max(0, (int)r.MousePosition.Y);
            return new MouseInput(x, y, delta > 0 ? MouseAction.WheelUp : MouseAction.WheelDown);
        }
        if ((r.EventFlags & MOUSE_MOVED) != 0)
            return new MouseInput(Math.Max(0, (int)r.MousePosition.X), Math.Max(0, (int)r.MousePosition.Y),
                MouseAction.Move); // движение: hover; по одному за Read, потопа нет
        if ((r.ButtonState & FROM_LEFT_1ST_BUTTON_PRESSED) == 0)
            return r.ButtonState == 0 && r.EventFlags == 0
                ? new MouseInput(Math.Max(0, (int)r.MousePosition.X), Math.Max(0, (int)r.MousePosition.Y),
                    MouseAction.Move) // отпускание: только позиция hover
                : null; // средняя/правая
        return new MouseInput(Math.Max(0, (int)r.MousePosition.X), Math.Max(0, (int)r.MousePosition.Y),
            MouseAction.LeftPress);
    }

    /// <summary>
    /// Вкл/выкл мышь conhost-API (.NET ReadKey её не отдаёт). Выкл возвращает
    /// и QuickEdit как было (RestoreInput при выходе вернёт вообще всё).
    /// </summary>
    public static void ApplyMouseInput(bool on)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!_stdinModeSaved)
            return; // консоль не наша (тесты, редирект) — режимы не трогаем
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
                // Гасим QuickEdit, иначе клики уходят в выделение conhost.
                // EXTENDED_FLAGS обязателен для смены QuickEdit.
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

    /// <summary>Вернуть режим ввода консоли (вызывать при выходе).</summary>
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
