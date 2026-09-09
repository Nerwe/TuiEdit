using System.Diagnostics;
using System.Text;

namespace TuiEdit;

internal abstract record InputEvent;

internal sealed record KeyInput(ConsoleKeyInfo Key) : InputEvent;

/// <summary>
/// Full-text paste: the terminal sends <c>ESC [ 200 ~ text ESC [ 201 ~</c>.
/// </summary>
internal sealed record PasteInput(string Text) : InputEvent;

/// <summary>Terminal window focus (?1004): true — focus-in (ESC[I), false — focus-out.</summary>
internal sealed record FocusInput(bool GotFocus) : InputEvent;

internal sealed class InputReader
{
    /// <summary>Mouse capture level (AppSettings.Mouse, off by default).</summary>
    public static MouseLevel MouseLevel { get; set; } = MouseLevel.Off;

    /// <summary>Mouse enabled (any level except Off).</summary>
    public static bool MouseEnabled => MouseLevel != MouseLevel.Off;

    /// <summary>Max tail chars after ESC (SGR mouse is actually shorter).</summary>
    private const int MaxBurst = 24;

    /// <summary>Wheel coalescing cap: one tick goes far, a burst of ticks is a single event.</summary>
    private const int MaxWheelCoalesce = 32;

    /// <summary>
    /// Already parsed events waiting their turn (coalescing deferred the "extra").
    /// </summary>
    private static readonly Queue<InputEvent> _pendingEvents = new();

    /// <summary>
    /// Speculative-read rollback: a tail without its own ESC is stored only together
    /// with a synthetic ESC in front (invariant: the queue is empty or starts with ESC).
    /// </summary>
    private static readonly Queue<ConsoleKeyInfo> _pendingKeys = new();

    public static InputEvent Read()
    {
        if (_pendingEvents.Count > 0)
            return _pendingEvents.Dequeue();
        if (MouseEnabled && OperatingSystem.IsWindows())
        {
            // We drain the conhost queue ourselves: .NET ReadKey swallows mouse events,
            // and blocks waiting for keys only. We call ReadKey only when a
            // key-down is at the front — then it returns instantly, losing nothing.
            while (true)
            {
                if (!Terminal.TryPeek(out Terminal.InputRecord rec))
                {
                    if (!Terminal.WaitForInput(50))
                        break; // console error — legacy path
                    continue;
                }
                if (rec.EventType == Terminal.MOUSE_EVENT)
                {
                    if (Terminal.Take() is MouseInput mev)
                    {
                        // Backpressure: stale motion (something already behind it)
                        // is eaten without a render — otherwise the flood starves keys.
                        if (mev.Action == MouseAction.Move && Terminal.PendingCount() > 0)
                            continue;
                        return mev;
                    }
                    continue;
                }
                if (rec.EventType != Terminal.KEY_EVENT || rec.KeyEvent.KeyDown == 0)
                {
                    Terminal.Take(); // key-up, resize, focus — skip
                    continue;
                }
                break; // key-down at the front
            }
        }
        ConsoleKeyInfo k = TakeKey();
        if (k.Key != ConsoleKey.Escape || !HasChar())
            return new KeyInput(k);
        var burst = new StringBuilder();
        // Read one by one and stop at the first complete sequence:
        // previously a burst greedily ate up to 24 chars, and fast input/paste
        // after a click was eaten and discarded together with garbage.
        while (HasChar() && burst.Length < MaxBurst)
        {
            burst.Append(TakeKey().KeyChar);
            string s = burst.ToString();
            if (s.StartsWith("[200~", StringComparison.Ordinal))
                return new PasteInput(ReadBracketedPaste(s[5..]));
            if ("[200~".StartsWith(s, StringComparison.Ordinal))
                continue; // strict prefix of the paste marker — wait for the tail
            if (s is "[I" or "[O")
            {
                var focus = new FocusInput(s == "[I");
                if (focus.GotFocus && MouseLevel != MouseLevel.Off)
                    Terminal.RestoreModes(MouseLevel); // ConPTY may have reset DEC modes
                return focus;
            }
            if (MouseEnabled && s.Length >= 2 && s[0] == '[' && s[1] == '<')
            {
                if (s[^1] is 'M' or 'm')
                    return FinishMouse(s, k);
                continue; // SGR mouse without a terminator — wait
            }
            break; // unknown CSI — as before: drop, return Esc
        }
        // Not paste — drop the burst so garbage does not get into the text.
        return new KeyInput(k);
    }

    /// <summary>
    /// Full SGR sequence: parse, drop middle/right into Esc
    /// (as before), coalesce wheel and motion with neighbors from the same chunk.
    /// </summary>
    private static InputEvent FinishMouse(string s, ConsoleKeyInfo esc)
    {
        if (MouseInput.TryParse(s) is not MouseInput m)
            return new KeyInput(esc); // broken SGR — as before: Esc
        if (m.Action == MouseAction.MiddlePress)
            return new KeyInput(esc); // no consumers — as before: Esc
        return Coalesce(m); // RightPress — into HandleMouseAt (copy by selection)
    }

    /// <summary>
    /// Coalesce events from one stdin chunk: same-direction wheel — into Count,
    /// motion — into the last position; a foreign event is deferred to the queue.
    /// </summary>
    private static MouseInput Coalesce(MouseInput first)
    {
        bool wheel = first.Action is MouseAction.WheelUp or MouseAction.WheelDown;
        if (!wheel && first.Action != MouseAction.Move)
            return first; // clicks — immediately, delay is unacceptable
        int count = first.Count, x = first.X, y = first.Y;
        while (HasChar())
        {
            string? seq = TakeRawSequence();
            if (seq is null)
                break; // not mouse or fragment — tail already rolled back, stop
            if (MouseInput.TryParse(seq) is not MouseInput m)
                break; // broken SGR — drop, stop
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
                y = m.Y; // hover: only the last position stays alive
                continue;
            }
            // Middle/right in the middle of a burst: as before — Esc, not an event.
            _pendingEvents.Enqueue(m.Action is MouseAction.MiddlePress or MouseAction.RightPress
                ? new KeyInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false))
                : m);
            break;
        }
        return first with { X = x, Y = y, Count = count };
    }

    /// <summary>
    /// Speculatively take one full SGR sequence ("[&lt;...M/m").
    /// Not mouse or a fragment — roll everything back (with a synthetic ESC in front)
    /// and return null, losing nothing.
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
                return null; // not mouse (arrow, paste, ...) — rollback
            }
            if (s.Length >= 2 && s[0] == '[' && s[1] == '<' && s[^1] is 'M' or 'm')
                return s; // full SGR mouse
        }
        PushBack(sb.ToString());
        return null; // fragment or overflow — rollback, parse later
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
                if (OperatingSystem.IsWindows())
                    Terminal.WaitForInput((int)Math.Min(50, 1500 - sw.ElapsedMilliseconds));
                else
                    Thread.Sleep(1); // no blocking console wait off Windows — keep the slice tiny
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
