using System.Text;
using TuiEdit;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch { }

var store = new SettingsStore(SettingsStore.ResolvePath());
AppSettings settings = store.Load();
if (!File.Exists(store.Path))
    store.Save(settings);
Loc loc = Loc.Load(settings.Language);
string? settingsDir = Path.GetDirectoryName(store.Path);
GrammarRegistry.EnsureLoaded(settingsDir is not null
    ? Path.Combine(settingsDir, "grammars")
    : GrammarRegistry.DefaultDir());

string? file = null;
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
            Console.WriteLine(loc["help.k1"]);
            Console.WriteLine(loc["help.k2"]);
            Console.WriteLine(loc["help.k3"]);
            Console.WriteLine(loc["help.k4"]);
            Console.WriteLine(loc["help.k5"]);
            Console.WriteLine(loc["help.k6"]);
            Console.WriteLine(loc["help.k7"]);
            Console.WriteLine(loc["help.k8"]);
            Console.WriteLine(loc["help.k9"]);
            Console.WriteLine(loc["help.k10"]);
            Console.WriteLine(loc["help.k11"]);
            Console.WriteLine(loc["help.k12"]);
            Console.WriteLine(loc["help.k13"]);
            Console.WriteLine(loc["help.k14"]);
            Console.WriteLine(loc["help.k15"]);
            Console.WriteLine(loc["help.k16"]);
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
            file ??= a;
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


