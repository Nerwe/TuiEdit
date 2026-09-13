namespace TuiEdit;

/// <summary>Represents a screen frame: cells (character plus colors) with diff output; renders each frame to memory and sends only changed runs to the console, so there is no flicker.</summary>
internal sealed class Screen
{
    public readonly record struct Cell(char Ch, Rgb Fg, Rgb Bg);

    /// <summary>Represents an output operation: a contiguous run of one color.</summary>
    public readonly record struct DrawOp(int X, int Y, string Text, Rgb Fg, Rgb Bg);

    /// <summary>Gets or sets a value that indicates whether Truecolor-ANSI output is used; <see langword="false"/> means the nearest 16 console colors.</summary>
    public bool TrueColor { get; set; } = true;

    private Cell[,] _cur = new Cell[0, 0];
    private Cell[,] _prev = new Cell[0, 0];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Resizes the screen (resets the previous frame - the first output is full).</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cur = new Cell[Width, Height];
        _prev = new Cell[Width, Height]; // '\0' is never written, so diff will be full
    }

    /// <summary>
    /// Maps a character to a safe cell glyph: control characters must never reach
    /// the console — a raw ESC makes the terminal swallow all following output,
    /// which looks like a total freeze (dead screen, blinking cursor, no errors).
    /// Tabs expand to a space (callers needing alignment expand them first);
    /// every other control becomes the replacement character (single cell).
    /// NUL passes through: it is the unwritten-cell sentinel (never emitted —
    /// the diff skips unchanged cells, and a stray NUL byte is ignored by terminals).
    /// </summary>
    internal static char SanitizeCell(char c) =>
        c == '\0' ? '\0' : c == '\t' ? ' ' : char.IsControl(c) ? '�' : c;

    /// <summary>Sets a cell (ignores off-screen positions; sanitizes control characters).</summary>
    public void Set(int x, int y, char ch, Rgb fg, Rgb bg)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            _cur[x, y] = new Cell(SanitizeCell(ch), fg, bg);
    }

    /// <summary>Reads a cell (for dimming the backdrop under dialogs).</summary>
    public Cell At(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _cur[x, y] : default;

    /// <summary>
    /// Snapshots the frame as text rows (unwritten cells read as spaces,
    /// trailing whitespace trimmed). For golden tests — no reflection needed.
    /// </summary>
    public List<string> Snapshot()
    {
        var rows = new List<string>(Height);
        var sb = new System.Text.StringBuilder(Width);
        for (int y = 0; y < Height; y++)
        {
            sb.Clear();
            for (int x = 0; x < Width; x++)
            {
                char c = _cur[x, y].Ch;
                sb.Append(c == '\0' ? ' ' : c);
            }
            rows.Add(sb.ToString().TrimEnd());
        }
        return rows;
    }

    public void Text(int x, int y, string text, Rgb fg, Rgb bg)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int i = 0; i < text.Length; i++)
            Set(x + i, y, text[i], fg, bg);
    }

    public void Fill(int x, int y, int count, char ch, Rgb fg, Rgb bg)
    {
        for (int i = 0; i < count; i++)
            Set(x + i, y, ch, fg, bg);
    }

    /// <summary>Builds the titled top frame of a modal exactly boxW wide (<c>┌─ Title ───┐</c>); uses one formula for all popups (the manager/settings used to be one symbol shorter).</summary>
    public static string TitleRow(string title, int boxW)
    {
        string seg = $" {title} ";
        if (seg.Length > boxW - 3)
            seg = seg[..Math.Max(0, boxW - 3)];
        return "┌─" + seg + new string('─', Math.Max(0, boxW - 3 - seg.Length)) + "┐";
    }

    /// <summary>Computes the diff against the previous frame as runs: adjacent changed cells of one color merge into one operation.</summary>
    public List<DrawOp> ComputeDiff()
    {
        var ops = new List<DrawOp>();
        for (int y = 0; y < Height; y++)
        {
            int x = 0;
            while (x < Width)
            {
                if (_cur[x, y].Equals(_prev[x, y]))
                {
                    x++;
                    continue;
                }
                Cell c = _cur[x, y];
                int start = x;
                var sb = new System.Text.StringBuilder();
                while (x < Width && !_cur[x, y].Equals(_prev[x, y]) && _cur[x, y].Fg == c.Fg && _cur[x, y].Bg == c.Bg)
                {
                    sb.Append(_cur[x, y].Ch);
                    x++;
                }
                ops.Add(new DrawOp(start, y, sb.ToString(), c.Fg, c.Bg));
            }
        }
        return ops;
    }

    /// <summary>Snapshots the frame as "previous" (copies - the current stays for increments).</summary>
    public void Swap() => _prev = (Cell[,])_cur.Clone();

    public void Flush()
    {
        List<DrawOp> ops = ComputeDiff();
        if (ops.Count == 0)
        {
            Swap();
            return;
        }
        try
        {
            if (TrueColor)
                FlushAnsi(ops);
            else
                FlushLegacy(ops);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException)
        {
            // Window was resized between frame and output - the next frame will fix it.
        }
        Swap();
    }

    /// <summary>
    /// Builds one ANSI payload for the whole diff: cursor addressing rides inline
    /// (CUP), so a full-viewport scroll costs a single console write, not ~600.
    /// Pure (no console access) for tests.
    /// </summary>
    internal static string BuildAnsiFrame(List<DrawOp> ops)
    {
        var sb = new System.Text.StringBuilder(ops.Count * 32);
        Rgb lastFg = default;
        Rgb lastBg = default;
        bool first = true;
        foreach (DrawOp op in ops)
        {
            sb.Append("\x1b[");
            sb.Append(op.Y + 1);
            sb.Append(';');
            sb.Append(op.X + 1);
            sb.Append('H');
            if (first || !op.Fg.Equals(lastFg))
            {
                sb.Append(op.Fg.ToAnsiFg());
                lastFg = op.Fg;
            }
            if (first || !op.Bg.Equals(lastBg))
            {
                sb.Append(op.Bg.ToAnsiBg());
                lastBg = op.Bg;
            }
            first = false;
            sb.Append(op.Text);
        }
        sb.Append("\x1b[0m");
        return sb.ToString();
    }

    private static void FlushAnsi(List<DrawOp> ops) => Console.Write(BuildAnsiFrame(ops));

    private static void FlushLegacy(List<DrawOp> ops)
    {
        ConsoleColor lastFg = (ConsoleColor)(-1);
        ConsoleColor lastBg = (ConsoleColor)(-1);
        foreach (DrawOp op in ops)
        {
            Console.SetCursorPosition(op.X, op.Y);
            ConsoleColor fg = op.Fg.ToConsoleColor();
            ConsoleColor bg = op.Bg.ToConsoleColor();
            if (fg != lastFg)
            {
                Console.ForegroundColor = fg;
                lastFg = fg;
            }
            if (bg != lastBg)
            {
                Console.BackgroundColor = bg;
                lastBg = bg;
            }
            Console.Write(op.Text);
        }
    }
}
