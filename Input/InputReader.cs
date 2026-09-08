using System.Diagnostics;
using System.Text;

namespace TuiEdit;

internal abstract record InputEvent;

internal sealed record KeyInput(ConsoleKeyInfo Key) : InputEvent;

    /// <summary>
    /// Вставка полным текстом: терминал присылает <c>ESC [ 200 ~ текст ESC [ 201 ~</c>.
    /// </summary>
    internal sealed record PasteInput(string Text) : InputEvent;

internal sealed class InputReader
{
    public InputEvent Read()
    {
        ConsoleKeyInfo k = Console.ReadKey(intercept: true);
        if (k.Key != ConsoleKey.Escape || !Console.KeyAvailable)
            return new KeyInput(k);
        var burst = new StringBuilder();
        while (Console.KeyAvailable && burst.Length < 16)
            burst.Append(Console.ReadKey(intercept: true).KeyChar);
        string s = burst.ToString();
        if (s.StartsWith("[200~", StringComparison.Ordinal))
            return new PasteInput(ReadBracketedPaste(s[5..]));
        // Не paste — пачку отбрасываем, чтобы мусор не попал в текст.
        return new KeyInput(k);
    }

    private static string ReadBracketedPaste(string head)
    {
        var sb = new StringBuilder(head);
        var tail = new StringBuilder();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            while (!Console.KeyAvailable)
            {
                if (sw.ElapsedMilliseconds > 1500)
                    return sb.ToString();
                Thread.Sleep(1);
            }
            char c = Console.ReadKey(intercept: true).KeyChar;
            sb.Append(c);
            tail.Append(c);
            if (tail.Length > 6)
                tail.Remove(0, 1);
            if (tail.ToString() == "\x1b[201~")
            {
                sb.Length -= 6;
                return sb.ToString();
            }
            sw.Restart();
        }
    }
}
