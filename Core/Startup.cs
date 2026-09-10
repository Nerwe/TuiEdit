namespace TuiEdit;

/// <summary>Parsed command line: help/version requests plus files and a sidebar root.</summary>
internal sealed record StartupPlan(
    bool ShowHelp,
    bool ShowVersion,
    List<(string path, int line, int col)> Files,
    string? StartDir);

/// <summary>
/// Startup plumbing shared by Program and tests: pure argument parsing plus
/// console help/version printers (redirectable) and terminal/home helpers.
/// </summary>
internal static class Startup
{
    /// <summary>Parses args into a plan (no console, no settings access).</summary>
    /// <param name="args">The raw command-line arguments.</param>
    public static StartupPlan ParseArgs(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var files = new List<(string path, int line, int col)>();
        string? startDir = null;
        bool help = false, version = false, decided = false;
        foreach (string a in args)
        {
            switch (a)
            {
                case "-h" or "--help" or "/?":
                    // First of help/version wins, like the old early returns.
                    if (!decided) { help = true; decided = true; }
                    break;
                case "-v" or "--version":
                    if (!decided) { version = true; decided = true; }
                    break;
                case ['-', ..]:
                    break; // unknown flags are ignored, as before
                default:
                    var (p, ln, cn) = CliArgs.SplitFileLine(a);
                    try
                    {
                        if (ln == 0 && cn == 0 && Directory.Exists(p))
                        {
                            startDir ??= p; // folder — sidebar root (no files)
                            break;
                        }
                    }
                    catch
                    {
                    }
                    files.Add((p, ln, cn));
                    break;
            }
        }
        return new StartupPlan(help, version, files, startDir);
    }

    /// <summary>Prints --help (order k1..k17, like the console help).</summary>
    /// <param name="loc">The localization.</param>
    /// <param name="settingsPath">The settings path shown in help.config.</param>
    public static void PrintHelp(Loc loc, string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(loc);
        Console.WriteLine(loc["help.title"]);
        Console.WriteLine();
        Console.WriteLine(loc["help.usage"]);
        Console.WriteLine(loc["help.usage.line"]);
        Console.WriteLine();
        Console.WriteLine(loc["help.keys"]);
        for (int i = 1; i <= 18; i++)
            Console.WriteLine(Dialog.StripSpans(loc[$"help.k{i}"]));
        Console.WriteLine();
        Console.WriteLine(loc["help.status"]);
        Console.WriteLine(loc.Format("help.config", ShortenHome(settingsPath)));
    }

    /// <summary>Prints --version.</summary>
    public static void PrintVersion() =>
        Console.WriteLine($"TuiEdit {TuiEditor.AppVersion} (net10.0, System.Console)");

    /// <summary>Restores a sane terminal (mirrors the crash handler; never throws).</summary>
    public static void RestoreTerminal()
    {
        try { Console.Write("\x1b[?2004l"); } catch { }
        try { Terminal.DisableMouse(); } catch { }
        try { Terminal.DisableFocusTracking(); } catch { }
        try { Terminal.RestoreInput(); } catch { }
        try { Console.ResetColor(); } catch { }
        try { Console.CursorVisible = true; } catch { }
        try { Console.TreatControlCAsInput = false; } catch { }
    }

    /// <summary>Shortens a home-prefixed path for display.</summary>
    /// <param name="path">The path to shorten.</param>
    public static string ShortenHome(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) &&
            (path.Equals(home, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            return "%USERPROFILE%" + path[home.Length..];
        return path;
    }
}
