namespace TuiEdit;

/// <summary>
/// Crash reports: best-effort durable traces for failures that used to kill
/// the editor silently (flaky mouse/terminal input, unexpected event errors).
/// Reports land in the temp dir (always writable, never blocks startup);
/// the UI points the user at the exact file.
/// </summary>
internal static class CrashLog
{
    private const int KeepReports = 20;

    /// <summary>Gets the crash report directory (created on write).</summary>
    public static string Dir =>
        Path.Combine(Path.GetTempPath(), "TuiEdit", "crashes");

    /// <summary>
    /// Writes a crash report. Returns the file path, or null when even that failed.
    /// Never throws.
    /// </summary>
    /// <param name="tag">A short tag identifying the failure site (e.g. "mouse").</param>
    /// <param name="ex">The caught exception.</param>
    public static string? Write(string tag, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        try
        {
            string dir = Dir;
            Directory.CreateDirectory(dir);
            string name = $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{tag}-{Guid.NewGuid():N}"[..40] + ".log";
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, $"{DateTime.UtcNow:O} [{tag}]{Environment.NewLine}{ex}");
            Prune(dir);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            string[] files = Directory.GetFiles(dir, "crash-*.log");
            if (files.Length <= KeepReports)
                return;
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length - KeepReports; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
