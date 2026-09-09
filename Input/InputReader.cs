using System.Diagnostics;
using System.Text;

namespace TuiEdit;

internal abstract record InputEvent;

internal sealed record KeyInput(ConsoleKeyInfo Key) : InputEvent;

/// <summary>
/// Вставка полным текстом: терминал присылает <c>ESC [ 200 ~ текст ESC [ 201 ~</c>.
/// </summary>
internal sealed record PasteInput(string Text) : InputEvent;

/// <summary>Фокус окна терминала (?1004): true — focus-in (ESC[I), false — focus-out.</summary>
internal sealed record FocusInput(bool GotFocus) : InputEvent;

internal sealed class InputReader
{
    /// <summary>Уровень захвата мыши (AppSettings.Mouse, выкл по умолчанию).</summary>
    public static MouseLevel MouseLevel { get; set; } = MouseLevel.Off;

    /// <summary>Мышь включена (любой уровень кроме Off).</summary>
    public static bool MouseEnabled => MouseLevel != MouseLevel.Off;

    /// <summary>Максимум символов хвоста после ESC (SGR-мышь реально короче).</summary>
    private const int MaxBurst = 24;

    /// <summary>Потолок склейки колеса: один тик — далеко, пачка тиков — одним событием.</summary>
    private const int MaxWheelCoalesce = 32;

    /// <summary>
    /// Уже разобранные события, ждущие своей очереди (коалесцирование отложило «лишнее»).
    /// </summary>
    private static readonly Queue<InputEvent> _pendingEvents = new();

    /// <summary>
    /// Откат спекулятивного чтения: хвост без своего ESC хранится только вместе
    /// с синтетическим ESC спереди (инвариант: очередь пуста или начинается с ESC).
    /// </summary>
    private static readonly Queue<ConsoleKeyInfo> _pendingKeys = new();

    public static InputEvent Read()
    {
        if (_pendingEvents.Count > 0)
            return _pendingEvents.Dequeue();
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
                    {
                        // Backpressure: устаревшее движение (за ним уже что-то есть)
                        // съедаем без рендера — иначе потоп морит клавиши голодом.
                        if (mev.Action == MouseAction.Move && Terminal.PendingCount() > 0)
                            continue;
                        return mev;
                    }
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
        ConsoleKeyInfo k = TakeKey();
        if (k.Key != ConsoleKey.Escape || !HasChar())
            return new KeyInput(k);
        var burst = new StringBuilder();
        // Читаем по одному и останавливаемся на первой полной последовательности:
        // раньше пачка жадно глотала до 24 символов, и быстрый ввод/вставка
        // после клика съедались и отбрасывались вместе с мусором.
        while (HasChar() && burst.Length < MaxBurst)
        {
            burst.Append(TakeKey().KeyChar);
            string s = burst.ToString();
            if (s.StartsWith("[200~", StringComparison.Ordinal))
                return new PasteInput(ReadBracketedPaste(s[5..]));
            if ("[200~".StartsWith(s, StringComparison.Ordinal))
                continue; // строгий префикс маркера вставки — ждём хвост
            if (s is "[I" or "[O")
            {
                var focus = new FocusInput(s == "[I");
                if (focus.GotFocus && MouseLevel != MouseLevel.Off)
                    Terminal.RestoreModes(MouseLevel); // ConPTY мог сбросить DEC-режимы
                return focus;
            }
            if (MouseEnabled && s.Length >= 2 && s[0] == '[' && s[1] == '<')
            {
                if (s[^1] is 'M' or 'm')
                    return FinishMouse(s, k);
                continue; // SGR-мышь без терминатора — ждём
            }
            break; // неизвестный CSI — как раньше: отбросить, вернуть Esc
        }
        // Не paste — пачку отбрасываем, чтобы мусор не попал в текст.
        return new KeyInput(k);
    }

    /// <summary>
    /// Полная SGR-последовательность: разобрать, среднюю/правую отбросить в Esc
    /// (как раньше), колесо и движение склеить с соседями из того же чанка.
    /// </summary>
    private static InputEvent FinishMouse(string s, ConsoleKeyInfo esc)
    {
        if (MouseInput.TryParse(s) is not MouseInput m)
            return new KeyInput(esc); // битый SGR — как раньше: Esc
        if (m.Action is MouseAction.MiddlePress or MouseAction.RightPress)
            return new KeyInput(esc); // потребителей нет — как раньше: Esc
        return Coalesce(m);
    }

    /// <summary>
    /// Склейка событий из одного stdin-чанка: колесо одного направления — в Count,
    /// движение — в последнюю позицию; чужое событие откладывается в очередь.
    /// </summary>
    private static MouseInput Coalesce(MouseInput first)
    {
        bool wheel = first.Action is MouseAction.WheelUp or MouseAction.WheelDown;
        if (!wheel && first.Action != MouseAction.Move)
            return first; // клики — сразу, задержка недопустима
        int count = first.Count, x = first.X, y = first.Y;
        while (HasChar())
        {
            string? seq = TakeRawSequence();
            if (seq is null)
                break; // не мышь или обрывок — хвост уже откачен, стоп
            if (MouseInput.TryParse(seq) is not MouseInput m)
                break; // битый SGR — отбросить, стоп
            if (wheel && m.Action == first.Action && count < MaxWheelCoalesce)
            {
                count += m.Count;
                x = m.X;
                y = m.Y;
                continue;
            }
            if (!wheel && m.Action == MouseAction.Move)
            {
                x = m.X;
                y = m.Y; // hover: жива только последняя позиция
                continue;
            }
            // Средняя/правая в середине пачки: как раньше — Esc, а не событие.
            _pendingEvents.Enqueue(m.Action is MouseAction.MiddlePress or MouseAction.RightPress
                ? new KeyInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false))
                : m);
            break;
        }
        return first with { X = x, Y = y, Count = count };
    }

    /// <summary>
    /// Спекулятивно забрать одну полную SGR-последовательность («[&lt;…M/m»).
    /// Не мышь или обрывок — откатить всё назад (с синтетическим ESC спереди)
    /// и вернуть null, ничего не потеряв.
    /// </summary>
    private static string? TakeRawSequence()
    {
        var sb = new StringBuilder();
        while (HasChar() && sb.Length < MaxBurst)
        {
            sb.Append(TakeKey().KeyChar);
            string s = sb.ToString();
            if (s.Length >= 2 && (s[0] != '[' || s[1] != '<'))
            {
                PushBack(sb.ToString());
                return null; // не мышь (стрелка, вставка, …) — откат
            }
            if (s.Length >= 2 && s[0] == '[' && s[1] == '<' && s[^1] is 'M' or 'm')
                return s; // полная SGR-мышь
        }
        PushBack(sb.ToString());
        return null; // обрывок или перебор — откат, разберём позже
    }

    private static void PushBack(string tail)
    {
        if (tail.Length == 0)
            return;
        _pendingKeys.Enqueue(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        foreach (char c in tail)
            _pendingKeys.Enqueue(new ConsoleKeyInfo(c, (ConsoleKey)c, false, false, false));
    }

    private static ConsoleKeyInfo TakeKey() =>
        _pendingKeys.Count > 0 ? _pendingKeys.Dequeue() : Console.ReadKey(intercept: true);

    private static bool HasChar() => _pendingKeys.Count > 0 || Terminal.IsKeyPending();

    private static string ReadBracketedPaste(string head)
    {
        var sb = new StringBuilder(head);
        var tail = new StringBuilder();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            while (!HasChar())
            {
                if (sw.ElapsedMilliseconds > 1500)
                    return sb.ToString();
                Thread.Sleep(1);
            }
            char c = TakeKey().KeyChar;
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
