using System.Text;
using TuiEdit;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch { }

var store = new SettingsStore(SettingsStore.ResolvePath());
AppSettings settings = store.Load();
if (!File.Exists(store.Path))
    store.Save(settings); // создать конфиг по умолчанию, чтобы было что править руками
Loc loc = Loc.Load(settings.Language);

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
            Console.WriteLine();
            Console.WriteLine(loc["help.status"]);
            Console.WriteLine(loc.Format("help.config", store.Path));
            return 0;
        case "-v" or "--version":
            Console.WriteLine("TuiEdit 0.1.0 (net10.0, System.Console)");
            return 0;
        case ['-', ..]:
            break; // неизвестный флаг — игнорируем
        default:
            file ??= a;
            break;
    }
}

Console.CancelKeyPress += (_, e) => e.Cancel = true; // Ctrl+C приходит как ввод (TreatControlCAsInput)

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine(loc["error.interactive"]);
    return 1;
}

var buffer = new TextBuffer(file);
var editor = new TuiEditor(buffer, settings, store);
editor.Run();
return 0;
