using System.Text;

namespace TuiEdit;

/// <summary>
/// Exports the internal clipboard to the system clipboard via OSC 52
/// (<c>"\x1b]52;c;" + base64 + "\x1b\\"</c>).
/// Without this, terminal Ctrl+V pastes the system clipboard,
/// which the editor does not track, so the copy is "lost".
/// </summary>
/// <summary>Reports how a clipboard export finished (for tests and future messages).</summary>
internal enum ClipboardExportResult
{
    SkippedEmpty,
    ExportedOsc52,
    ExportedTool,
    InternalOnly,
}

internal static class SystemClipboard
{
    private const int MaxSyncChars = 128 * 1024; // Mirrors LARGE_CLIPBOARD_THRESHOLD in Edit

    /// <summary>Environment lookup (overridable in tests).</summary>
    internal static Func<string, string?> Env { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>OSC 52 writer (overridable in tests to avoid polluting output).</summary>
    internal static Func<string, bool> Osc52Writer { get; set; } = DefaultOsc52Writer;

    /// <summary>External tool runner: (file, args, stdin) to success (overridable in tests).</summary>
    internal static Func<string, string[], string, bool> ToolRunner { get; set; } = DefaultToolRunner;

    /// <summary>Builds the OSC 52 sequence for lines (returns null when empty/too large).</summary>
    public static string? BuildOsc52(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string text = string.Join("\n", lines);
        if (text.Length == 0 || text.Length > MaxSyncChars)
            return null;
        return "\x1b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\x1b\\";
    }

    /// <summary>Checks whether the terminal likely supports OSC 52 (WT plus common emulators).</summary>
    public static bool Osc52Supported()
    {
        if (Env("WT_SESSION") is not null)
            return true;
        if (Env("KITTY_WINDOW_ID") is not null || Env("KITTY_PID") is not null)
            return true;
        if (Env("WEZTERM_PANE") is not null)
            return true;
        if (Env("VSCODE_INJECTION") is not null || Env("VSCODE_GIT_ASKPASS_NODE") is not null)
            return true;
        string? program = Env("TERM_PROGRAM");
        if (program is not null)
        {
            string p = program.ToLowerInvariant();
            if (p is "wezterm" or "kitty" or "ghostty" or "vscode" or "iterm.app" or "foot" or "alacritty")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Attempts best-effort sync with fallbacks: OSC 52, then a platform tool
    /// (clip/powershell, pbcopy, wl-copy/xclip/xsel), then internal-only.
    /// Never throws; large texts skip OSC 52 and go straight to tools.
    /// </summary>
    public static ClipboardExportResult TryExportWithResult(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string text = string.Join("\n", lines);
        if (text.Length == 0)
            return ClipboardExportResult.SkippedEmpty;
        if (text.Length <= MaxSyncChars && Osc52Supported())
        {
            string? seq = BuildOsc52(lines);
            if (seq is not null)
            {
                try
                {
                    if (Osc52Writer(seq))
                        return ClipboardExportResult.ExportedOsc52;
                }
                catch
                {
                    // Fall through to external tools.
                }
            }
        }
        try
        {
            if (TryExportTool(text))
                return ClipboardExportResult.ExportedTool;
        }
        catch
        {
            // Fall through to internal-only.
        }
        return ClipboardExportResult.InternalOnly;
    }

    /// <summary>
    /// Attempts best-effort sync (quietly). Keeps the internal clipboard always;
    /// the system copy is a bonus via OSC 52 or a platform tool.
    /// </summary>
    public static void TryExport(IEnumerable<string> lines)
    {
        _ = TryExportWithResult(lines);
    }

    private static bool DefaultOsc52Writer(string seq)
    {
        try
        {
            Console.Write(seq);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryExportTool(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            if (ToolRunner("clip.exe", [], text))
                return true;
            return ToolRunner("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "Set-Clipboard"], text);
        }
        if (OperatingSystem.IsMacOS())
            return ToolRunner("pbcopy", [], text);
        // Linux and other Unix: Wayland first, then X11 helpers.
        string? wayland = Env("WAYLAND_DISPLAY");
        string? session = Env("XDG_SESSION_TYPE");
        if (wayland is not null || string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            if (ToolRunner("wl-copy", [], text))
                return true;
        }
        if (ToolRunner("xclip", ["-selection", "clipboard"], text))
            return true;
        return ToolRunner("xsel", ["--clipboard", "--input"], text);
    }

    private static bool DefaultToolRunner(string file, string[] args, string stdinText)
    {
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = file,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
                proc.StartInfo.ArgumentList.Add(a);
            if (!proc.Start())
                return false;
            try
            {
                proc.StandardInput.Write(stdinText);
            }
            catch
            {
                // The tool may exit early; treat close as best-effort.
            }
            finally
            {
                try { proc.StandardInput.Close(); } catch { }
            }
            return proc.WaitForExit(2000) && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
