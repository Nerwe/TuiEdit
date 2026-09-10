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

    StartupPlan plan = Startup.ParseArgs(args);
    if (plan.ShowHelp)
    {
        Startup.PrintHelp(loc, store.Path);
        return (loc, 0, null);
    }
    if (plan.ShowVersion)
    {
        Startup.PrintVersion();
        return (loc, 0, null);
    }
    var files = plan.Files;
    string? startDir = plan.StartDir;

    Console.CancelKeyPress += (_, e) => e.Cancel = true;

    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        Console.Error.WriteLine(loc["error.interactive"]);
        return (loc, 1, null);
    }

    InputReader.MouseLevel = settings.Mouse; // mouse off by default
    string? firstPath = files.Count > 0 ? files[0].path : null;
    TextBuffer buffer;
    string? openError = null;
    try
    {
        buffer = new TextBuffer(firstPath);
    }
    catch (Exception ex)
    {
        // Unreadable first file: start empty and say why (like LoadFile does in-app).
        buffer = new TextBuffer(null);
        openError = ex.Message;
    }
    // Composition root: services are wired explicitly (no container — single-file app).
    // Overrides are compiled into KeyMap.Current above, so the editor's table matches menu hints.
    var editor = new TuiEditor(buffer, settings, store,
        GitService.Shared, SystemClipboardService.Shared, KeyMap.Current);
    editor.TrackKeyBindings(keyBindingsPath);
    if (openError is not null && firstPath is not null)
        editor.Notify(loc.Format("error.openfile", firstPath, openError));
    try
    {
        string corrupt = store.Path + ".corrupt";
        if (File.Exists(corrupt))
            editor.Notify(loc.Format("msg.settings.corrupt", corrupt));
    }
    catch
    {
    }
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

static void RestoreTerminal() => Startup.RestoreTerminal();


