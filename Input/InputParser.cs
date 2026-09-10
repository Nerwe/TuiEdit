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

/// <summary>
/// Pure stdin-sequence parser: escape bursts, SGR mouse, bracketed paste, focus reports.
/// Console access goes through injected delegates, so the whole machine (including
/// chunk coalescing and rollback) is testable without a terminal. Production wires
/// <see cref="Terminal"/> peeks and blocking <see cref="Console"/> reads.
/// </summary>
internal sealed class InputParser
{
    /// <summary>Max tail chars after ESC (SGR mouse is actually shorter).</summary>
    private const int MaxBurst = 24;

    /// <summary>Wheel coalescing cap: one tick goes far, a burst of ticks is a single event.</summary>
    private const int MaxWheelCoalesce = 32;

    private readonly Queue<InputEvent> _pendingEvents = new();

    /// <summary>
    /// Speculative-read rollback: a tail without its own ESC is stored only together
    /// with a synthetic ESC in front (invariant: the queue is empty or starts with ESC).
    /// </summary>
    private readonly Queue<ConsoleKeyInfo> _keys = new();

    private readonly Func<bool> _hasConsoleChar;
    private readonly Func<ConsoleKeyInfo> _readConsoleKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputParser"/> class.
    /// </summary>
    /// <param name="hasConsoleChar">Non-blocking console peek (no side effects).</param>
    /// <param name="readConsoleKey">Console read (may block; called only when input is pending).</param>
    public InputParser(Func<bool> hasConsoleChar, Func<ConsoleKeyInfo> readConsoleKey)
    {
        ArgumentNullException.ThrowIfNull(hasConsoleChar);
        ArgumentNullException.ThrowIfNull(readConsoleKey);
        _hasConsoleChar = hasConsoleChar;
        _readConsoleKey = readConsoleKey;
    }

    /// <summary>Feeds one key (console input or synthetic test input).</summary>
    /// <param name="k">The key to queue.</param>
    public void Feed(ConsoleKeyInfo k) => _keys.Enqueue(k);

    /// <summary>Clears all queued input (tests).</summary>
    public void Clear()
    {
        _pendingEvents.Clear();
        _keys.Clear();
    }

    /// <summary>Whether queued (not console) input remains.</summary>
    public bool HasQueued => _pendingEvents.Count > 0 || _keys.Count > 0;

    private ConsoleKeyInfo TakeKey() =>
        _keys.Count > 0 ? _keys.Dequeue() : _readConsoleKey();

    private bool HasChar() => _keys.Count > 0 || _hasConsoleChar();

    /// <summary>
    /// Parses one event from queued input. Returns null only when both queues are empty
    /// and the console has nothing pending (the caller should block on the console then).
    /// Never blocks itself: the console is only peeked, and read when a char is pending.
    /// </summary>
    /// <param name="mouseEnabled">Whether SGR mouse sequences are recognized.</param>
    public InputEvent? TryRead(bool mouseEnabled)
    {
        if (_pendingEvents.Count > 0)
            return _pendingEvents.Dequeue();
        if (_keys.Count == 0 && !HasChar())
            return null;
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
                if (focus.GotFocus && mouseEnabled)
                    Terminal.RestoreModes(InputReader.MouseLevel); // ConPTY may have reset DEC modes
                return focus;
            }
            if (mouseEnabled && s.Length >= 2 && s[0] == '[' && s[1] == '<')
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
    private InputEvent FinishMouse(string s, ConsoleKeyInfo esc)
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
    private MouseInput Coalesce(MouseInput first)
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
    private string? TakeRawSequence()
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

    private void PushBack(string tail)
    {
        if (tail.Length == 0)
            return;
        _keys.Enqueue(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        foreach (char c in tail)
            _keys.Enqueue(new ConsoleKeyInfo(c, (ConsoleKey)c, false, false, false));
    }

    private string ReadBracketedPaste(string head)
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
