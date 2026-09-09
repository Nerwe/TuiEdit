using System.Text;

namespace TuiEdit;

/// <summary>
/// Exports the internal clipboard to the system clipboard via OSC 52
/// (<c>"\x1b]52;c;" + base64 + "\x1b\\"</c>).
/// Without this, terminal Ctrl+V pastes the system clipboard,
/// which the editor does not track, so the copy is "lost".
/// </summary>
internal static class SystemClipboard
{
    private const int MaxSyncChars = 128 * 1024; // Mirrors LARGE_CLIPBOARD_THRESHOLD in Edit

    /// <summary>Builds the OSC 52 sequence for lines (returns null when empty/too large).</summary>
    public static string? BuildOsc52(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string text = string.Join("\n", lines);
        if (text.Length == 0 || text.Length > MaxSyncChars)
            return null;
        return "\x1b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\x1b\\";
    }

    /// <summary>
    /// Attempts best-effort sync (quietly). Targets Windows Terminal only
    /// (<c>WT_SESSION</c>): conhost does not understand OSC 52, so it must not pollute output.
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
