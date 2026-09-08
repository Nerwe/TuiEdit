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
    /// <summary>Мышь включена (AppSettings.EnableMouse, выкл по умолчанию).</summary>
    public static bool MouseEnabled { get; set; }

    public static InputEvent Read()
    {
        if (MouseEnabled && OperatingSystem.IsWindows())
        {
            // Очередь conhost разбираем сами: .NET ReadKey события мыши глотает,
            // а в блокировке ждёт только клавиш. ReadKey зовём лишь когда спереди
            // key-down — тогда он возвращается мгновенно, ничего не теряя.
            while (true)
            {
                if (!Terminal.TryPeek(out Terminal.InputRecord rec))
                {
                    if (!Terminal.WaitForInput(50))
                        break; // ошибка консоли — старый путь
                    continue;
                }
                if (rec.EventType == Terminal.MOUSE_EVENT)
                {
                    if (Terminal.Take() is MouseInput mev)
                        return mev.Action == MouseAction.Move ? CoalesceMove(mev) : mev;
                    continue;
                }
                if (rec.EventType != Terminal.KEY_EVENT || rec.KeyEvent.KeyDown == 0)
                {
                    Terminal.Take(); // key-up, ресайз, фокус — мимо
                    continue;
                }
                break; // спереди key-down
            }
        }
        ConsoleKeyInfo k = Console.ReadKey(intercept: true);
        if (k.Key != ConsoleKey.Escape || !Console.KeyAvailable)
            return new KeyInput(k);
        var burst = new StringBuilder();
        // Готовность — через IsKeyPending: голый KeyAvailable на Windows истинен
        // и на мышиных записях, а ReadKey поверх них блокируется и ест клавиши.
        while (Terminal.IsKeyPending() && burst.Length < 24)
        {
            burst.Append(Console.ReadKey(intercept: true).KeyChar);
            if (burst.Length >= 16 && (burst.Length < 2 || burst[1] != '<'))
                break; // длиннее 16 — только хвост SGR-мыши, остальное как раньше
        }
        string s = burst.ToString();
        if (MouseEnabled && MouseInput.TryParse(s) is MouseInput m)
            return m;
        if (s.StartsWith("[200~", StringComparison.Ordinal))
            return new PasteInput(ReadBracketedPaste(s[5..]));
        // Не paste — пачку отбрасываем, чтобы мусор не попал в текст.
        return new KeyInput(k);
    }

    /// <summary>
    /// Склеить пачку движений в последнее (иначе потоп motion-событий морит
    /// клавиши голодом: каждое движение тянуло бы полный Render).
    /// Клик/колесо внутри пачки — сразу наружу (важнее), лимит 16.
    /// </summary>
    private static MouseInput CoalesceMove(MouseInput first)
    {
        MouseInput m = first;
        for (int i = 0; i < 16; i++)
        {
            if (!Terminal.TryPeek(out Terminal.InputRecord rec) || rec.EventType != Terminal.MOUSE_EVENT)
                break;
            MouseInput? next = Terminal.Take();
            if (next is null)
                continue;
            if (next.Action != MouseAction.Move)
                return next;
            m = next;
        }
        return m;
    }

    private static string ReadBracketedPaste(string head)
    {
        var sb = new StringBuilder(head);
        var tail = new StringBuilder();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            while (!Terminal.IsKeyPending())
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
