using System.Diagnostics;
using System.Text;

namespace TuiEdit;

/// <summary>Событие ввода.</summary>
internal abstract record InputEvent;

/// <summary>Нажатие клавиши.</summary>
internal sealed record KeyInput(ConsoleKeyInfo Key) : InputEvent;

/// <summary>
/// Вставка полным текстом (bracketed paste, как <c>Input::Paste</c> в MS Edit:
/// терминал присылает <c>ESC [ 200 ~ текст ESC [ 201 ~</c>).
/// </summary>
internal sealed record PasteInput(string Text) : InputEvent;

/// <summary>
/// Чтение ввода: клавиши идут как есть, bracketed-paste собирается
/// из escape-последовательности в одно событие вставки полным текстом.
/// </summary>
internal sealed class InputReader
{
    /// <summary>Прочитать следующее событие ввода (блокирующий вызов).</summary>
    /// <exception cref="InvalidOperationException">Ввод перенаправлен.</exception>
    public InputEvent Read()
    {
        ConsoleKeyInfo k = Console.ReadKey(intercept: true);
        if (k.Key != ConsoleKey.Escape || !Console.KeyAvailable)
            return new KeyInput(k);
        // За Esc сразу идут байты — забираем пачку не блокируясь.
        var burst = new StringBuilder();
        while (Console.KeyAvailable && burst.Length < 16)
            burst.Append(Console.ReadKey(intercept: true).KeyChar);
        string s = burst.ToString();
        if (s.StartsWith("[200~", StringComparison.Ordinal))
            return new PasteInput(ReadBracketedPaste(s[5..]));
        // Не paste (на Windows-консоли такого быть не должно) — пачку отбрасываем,
        // чтобы мусор последовательностей не попал в текст, Esc отдаём как есть.
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
                    return sb.ToString(); // терминатор не дождались — отдаём как есть
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
