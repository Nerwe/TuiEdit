using System.Runtime.InteropServices;

namespace TuiEdit;

/// <summary>
/// Режимы терминала: включение VT-последовательностей
/// (нужны truecolor-цветам и bracketed paste).
/// </summary>
internal static class Terminal
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    /// <summary>
    /// Включить обработку VT-последовательностей для вывода.
    /// На Unix VT есть изначально (кроме dumb-терминалов).
    /// </summary>
    /// <returns>Можно ли использовать truecolor-ANSI.</returns>
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
}
