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
    // Аварийный выход: терминал в исходное, черновики в драфты, короткий текст.
    // (Run() уже чинит терминал в finally — здесь прикрываем стартовую фазу.)
    RestoreTerminal();
    int dumped = 0;
    try { dumped = editor?.EmergencyDump() ?? 0; } catch { }
    string msg = $"Unexpected error. Drafts dumped: {dumped}.";
    try { if (loc is not null) msg = loc.Format("error.crash", dumped); } catch { }
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
    try
    {
        string kb = KeyBindings.DefaultPath(settingsDir);
        KeyBindings.SeedExample(kb);
        KeyMap.SetOverrides(KeyBindings.Load(kb));
    }
    catch
    {
    }

    string? file = null;
    int gotoLine = 0;
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
                if (file is null)
                    (file, gotoLine) = CliArgs.SplitFileLine(a);
                break;
        }
    }

    Console.CancelKeyPress += (_, e) => e.Cancel = true;

    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.Error.WriteLine(loc["error.interactive"]);
        return (loc, 1, null);
    }

    InputReader.MouseLevel = settings.Mouse; // мышь выкл по умолчанию
    var buffer = new TextBuffer(file);
    var editor = new TuiEditor(buffer, settings, store);
    if (file is null)
        editor.RestoreSessionTabs();
    else if (gotoLine > 0)
        editor.GoToLineNumber(gotoLine);
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
