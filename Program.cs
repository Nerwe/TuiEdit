using System.Text;
using TuiEdit;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch { }

TuiEditor? editor = null;
Loc? loc = null;
try
{
    int exit;
    (loc, exit, editor) = RunApp(args);
    return exit;
}
catch (Exception ex)
{
    // Emergency exit: restore the terminal, dump drafts, short message.
    // (Run() already fixes the terminal in finally — this covers the startup phase.)
    RestoreTerminal();
    int dumped = 0;
    try { dumped = editor?.EmergencyDump() ?? 0; } catch { }
    string msg = $"Unexpected error. Drafts dumped: {dumped}.";
    try { if (loc is not null) msg = loc.Format("error.crash", dumped); } catch { }
    try
    {
        string? log = CrashLog.Write("fatal", ex);
        if (log is not null)
            msg += loc is not null ? " " + loc.Format("error.crashlog", log) : " Log: " + log;
    }
    catch { }
    try { Console.Error.WriteLine(msg); } catch { }
    try
    {
        if (Environment.GetEnvironmentVariable("TUIEDIT_DEBUG") == "1")
            Console.Error.WriteLine(ex.ToString());
    }
    catch { }
    return 1;
}

static (Loc loc, int exit, TuiEditor? editor) RunApp(string[] args)
{
    var store = new SettingsStore(SettingsStore.ResolvePath());
    AppSettings settings = store.Load();
    if (!File.Exists(store.Path))
        store.Save(settings);
    Loc loc = Loc.Load(settings.Language);
    new BackupStore(BackupStore.DefaultDir(store.Path)).PruneAll();
    string? settingsDir = Path.GetDirectoryName(store.Path);
    GrammarRegistry.EnsureLoaded(settingsDir is not null
        ? Path.Combine(settingsDir, "grammars")
        : GrammarRegistry.DefaultDir());
    string? keyBindingsPath = null;
    try
    {
        string kb = KeyBindings.DefaultPath(settingsDir);
        KeyBindings.SeedExample(kb);
        KeyMap.SetOverrides(KeyBindings.Load(kb));
        keyBindingsPath = kb;
    }
    catch
    {
    }

    var files = new List<(string path, int line, int col)>();
    string? startDir = null;
    foreach (string a in args)
    {
        switch (a)
        {
            case "-h" or "--help" or "/?":
                Console.WriteLine(loc["help.title"]);
                Console.WriteLine();
                Console.WriteLine(loc["help.usage"]);
                Console.WriteLine(loc["help.usage.line"]);
                Console.WriteLine();
                Console.WriteLine(loc["help.keys"]);
                void H(string key) => Console.WriteLine(Dialog.StripSpans(loc[key]));
                H("help.k1");
                H("help.k2");
                H("help.k3");
                H("help.k4");
                H("help.k5");
                H("help.k6");
                H("help.k7");
                H("help.k8");
                H("help.k9");
                H("help.k10");
                H("help.k11");
                H("help.k12");
                H("help.k13");
                H("help.k14");
                H("help.k15");
                H("help.k16");
                H("help.k17");
                Console.WriteLine();
                Console.WriteLine(loc["help.status"]);
                Console.WriteLine(loc.Format("help.config", ShortenHome(store.Path)));
                return (loc, 0, null);
            case "-v" or "--version":
                Console.WriteLine($"TuiEdit {TuiEditor.AppVersion} (net10.0, System.Console)");
                return (loc, 0, null);
            case ['-', ..]:
                break;
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

    Console.CancelKeyPress += (_, e) => e.Cancel = true;

    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.Error.WriteLine(loc["error.interactive"]);
        return (loc, 1, null);
    }

    InputReader.MouseLevel = settings.Mouse; // mouse off by default
    string? firstPath = files.Count > 0 ? files[0].path : null;
    var buffer = new TextBuffer(firstPath);
    // Composition root: services are wired explicitly (no container — single-file app).
    // Overrides are compiled into KeyMap.Current above, so the editor's table matches menu hints.
    var editor = new TuiEditor(buffer, settings, store,
        GitService.Shared, SystemClipboardService.Shared, KeyMap.Current);
    editor.TrackKeyBindings(keyBindingsPath);
    if (firstPath is null)
    {
        editor.RestoreSessionTabs();
        if (startDir is not null)
            editor.OpenSidebarRoot(startDir);
    }
    else
    {
        if (files[0].line > 0)
            editor.GoToPosition(files[0].line, files[0].col);
        for (int i = 1; i < files.Count; i++)
            editor.OpenStartupFile(files[i].path, files[i].line, files[i].col);
    }
    editor.Run();
    return (loc, 0, editor);
}

static void RestoreTerminal()
{
    try { Console.Write("\x1b[?2004l"); } catch { }
    try { Terminal.DisableMouse(); } catch { }
    try { Terminal.DisableFocusTracking(); } catch { }
    try { Terminal.RestoreInput(); } catch { }
    try { Console.ResetColor(); } catch { }
    try { Console.CursorVisible = true; } catch { }
    try { Console.TreatControlCAsInput = false; } catch { }
}

static string ShortenHome(string path)
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrEmpty(home) &&
        (path.Equals(home, StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        return "%USERPROFILE%" + path[home.Length..];
    return path;
}
