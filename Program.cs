using System.Text;
using TuiEdit;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch { }

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
            return 0;
        case "-v" or "--version":
            Console.WriteLine($"TuiEdit {TuiEditor.AppVersion} (net10.0, System.Console)");
            return 0;
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
    return 1;
}

var buffer = new TextBuffer(file);
var editor = new TuiEditor(buffer, settings, store);
if (file is null)
    editor.RestoreSessionTabs();
else if (gotoLine > 0)
    editor.GoToLineNumber(gotoLine);
editor.Run();
return 0;

static string ShortenHome(string path)
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrEmpty(home) &&
        (path.Equals(home, StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        return "%USERPROFILE%" + path[home.Length..];
    return path;
}

