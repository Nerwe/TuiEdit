using System.Text;

namespace TuiEdit;

/// <summary>
/// Экспорт внутреннего буфера обмена в системный через OSC 52
/// (<c>"\x1b]52;c;" + base64 + "\x1b\\"</c>).
/// Без этого Ctrl+V терминала вставляет системный буфер,
/// о котором редактор ничего не знает, и копия «теряется».
/// </summary>
public static class SystemClipboard
{
    private const int MaxSyncChars = 128 * 1024; // как LARGE_CLIPBOARD_THRESHOLD в Edit

    /// <summary>OSC 52-последовательность для строк (null — пусто/слишком велико).</summary>
    public static string? BuildOsc52(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string text = string.Join("\n", lines);
        if (text.Length == 0 || text.Length > MaxSyncChars)
            return null;
        return "\x1b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\x1b\\";
    }

    /// <summary>
    /// Best-effort синхронизация (тихо). Только Windows Terminal
    /// (<c>WT_SESSION</c>): conhost OSC 52 не понимает, мусорить в вывод нельзя.
    /// </summary>
    public static void TryExport(IEnumerable<string> lines)
    {
        if (Environment.GetEnvironmentVariable("WT_SESSION") is null)
            return;
        string? seq = BuildOsc52(lines);
        if (seq is null)
            return;
        try
        {
            Console.Write(seq);
        }
        catch (IOException)
        {
        }
    }
}
