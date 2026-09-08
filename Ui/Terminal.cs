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

    private static uint _stdinOldMode;
    private static bool _stdinModeSaved;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

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
