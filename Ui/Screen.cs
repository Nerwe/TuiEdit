namespace TuiEdit;

/// <summary>Represents a screen frame: cells (character plus colors) with diff output; renders each frame to memory and sends only changed runs to the console, so there is no flicker.</summary>
public sealed class Screen
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

    /// <summary>Sets a cell (ignores off-screen positions).</summary>
    public void Set(int x, int y, char ch, Rgb fg, Rgb bg)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            _cur[x, y] = new Cell(ch, fg, bg);
    }

    /// <summary>Reads a cell (for dimming the backdrop under dialogs).</summary>
    public Cell At(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _cur[x, y] : default;

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

    private static void FlushAnsi(List<DrawOp> ops)
    {
        Rgb lastFg = default;
        Rgb lastBg = default;
        bool first = true;
        foreach (DrawOp op in ops)
        {
            Console.SetCursorPosition(op.X, op.Y);
            if (first || !op.Fg.Equals(lastFg))
            {
                Console.Write(op.Fg.ToAnsiFg());
                lastFg = op.Fg;
            }
            if (first || !op.Bg.Equals(lastBg))
            {
                Console.Write(op.Bg.ToAnsiBg());
                lastBg = op.Bg;
            }
            first = false;
            Console.Write(op.Text);
        }
        Console.Write("\x1b[0m");
    }

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
