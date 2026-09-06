using System.Text;
using TuiEdit;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch { }

string? file = null;
foreach (string a in args)
{
    if (a is "-h" or "--help" or "/?")
    {
        Console.WriteLine("TuiEdit — простой TUI текстовый редактор (стиль Microsoft Edit / nano).");
        Console.WriteLine();
        Console.WriteLine("Использование:");
        Console.WriteLine("  tui-edit [файл]");
        Console.WriteLine();
        Console.WriteLine("Горячие клавиши:");
        Console.WriteLine("  F2 или ^S сохранить (без имени — запросит)   ^O сохранить как   ^Q выход");
        Console.WriteLine("  ^F найти       F3 далее           ^G перейти к строке");
        Console.WriteLine("  ^K вырезать строку  ^U/^V вставить  ^C копировать строку");
        Console.WriteLine("  ^Z отмена  ^Y возврат   стрелки/Home/End/PgUp/PgDn");
        Console.WriteLine("  Ctrl+стрелки — по словам, Enter — новая строка, Tab — 4 пробела");
        Console.WriteLine();
        Console.WriteLine("Примечание: в классической консоли Windows ^S может");
        Console.WriteLine("перехватываться как пауза вывода (XOFF) — тогда жмите F2.");
        return 0;
    }
    if (a is "-v" or "--version")
    {
        Console.WriteLine("TuiEdit 0.1.0 (net10.0, System.Console)");
        return 0;
    }
    if (!a.StartsWith('-'))
        file ??= a;
}

Console.CancelKeyPress += (_, e) => e.Cancel = true; // Ctrl+C приходит как ввод (TreatControlCAsInput)

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("TuiEdit требует интерактивную консоль (ввод/вывод перенаправлен).");
    return 1;
}

var buffer = new TextBuffer(file);
var editor = new TuiEditor(buffer);
editor.Run();
return 0;
