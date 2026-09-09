namespace TuiEdit;

public readonly record struct DialogBox(int X0, int Y0, int W, int H);

/// <summary>Provides the base dialog window: centering, a titled frame, and an input loop with a focus trap.</summary>
internal abstract class Dialog
{
    /// <summary>Gets a value that indicates whether the dialog is closed - the Render/Read loop then exits.</summary>
    public bool Closed { get; protected set; }

    protected abstract string GetTitle(Loc loc);

    /// <summary>Measures the centered content frame (returns null when it does not fit); every heir mirrors its former size formula exactly.</summary>
    protected abstract DialogBox? Measure(int screenW, int screenH, Loc loc);

    /// <summary>Draws content over the empty frame (inner width is box.W - 2).</summary>
    protected abstract void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box);

    protected virtual (Rgb fg, Rgb bg) FrameColors(Theme theme) => (theme.ModalFg, theme.ModalBg);

    /// <summary>Gets the frame title color (accent by default).</summary>
    protected virtual Rgb TitleFg(Theme theme) => theme.AccentFg;

    /// <summary>Gets the dimmed frame hint color (esc).</summary>
    protected virtual Rgb HintFg(Theme theme) => theme.ModalHintFg;

    public void Draw(Screen screen, Theme theme, Loc loc)
    {
        DialogBox? box = Measure(screen.Width, screen.Height, loc);
        if (box is null)
            return;
        DimBackdrop(screen);
        var (fg, bg) = FrameColors(theme);
        DrawFrame(screen, GetTitle(loc), fg, bg, box.Value, TitleFg(theme), HintFg(theme));
        DrawContent(screen, theme, loc, fg, bg, box.Value);
    }

    /// <summary>
    /// Dims the backdrop under the window (makes the window "pop"). Truecolor only;
    /// skips legacy mode. Stable frame after frame: the editor
    /// underneath redraws fresh on every Render.
    /// </summary>
    protected static void DimBackdrop(Screen screen)
    {
        if (!screen.TrueColor)
            return;
        var black = new Rgb(0, 0, 0);
        for (int y = 0; y < screen.Height; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                Screen.Cell c = screen.At(x, y);
                screen.Set(x, y, c.Ch, c.Fg.Blend(black, 0.55), c.Bg.Blend(black, 0.55));
            }
        }
    }

    private static void DrawFrame(Screen screen, string title, Rgb fg, Rgb bg, DialogBox box, Rgb titleFg, Rgb hintFg)
    {
        screen.Text(box.X0, box.Y0, Screen.TitleRow(title, box.W), fg, bg);
        int maxT = Math.Max(0, Math.Min(title.Length, box.W - 4));
        if (maxT > 0)
            screen.Text(box.X0 + 3, box.Y0, title[..maxT], titleFg, bg);
        if (box.W >= 16 && maxT + 9 <= box.W)
            screen.Text(box.X0 + box.W - 4, box.Y0, "esc", hintFg, bg);
        for (int y = box.Y0 + 1; y < box.Y0 + box.H - 1; y++)
            screen.Text(box.X0, y, "│" + new string(' ', box.W - 2) + "│", fg, bg);
        screen.Text(box.X0, box.Y0 + box.H - 1, "└" + new string('─', box.W - 2) + "┘", fg, bg);
    }

    /// <summary>Handles a key (focus trap: swallows everything).</summary>
    public abstract void HandleKey(ConsoleKeyInfo key);

    /// <summary>Handles a click: returns <see langword="true"/> if handled (including swallowing), otherwise <see langword="false"/> for a miss.</summary>
    public virtual bool HandleClick(int x, int y, int screenW, int screenH, Loc loc) => false;

    /// <summary>Pastes from the clipboard (ignores by default).</summary>
    public virtual void Paste(string text)
    {
    }

    /// <summary>Cancels from the outside without an outcome (e.g. console loss).</summary>
    public virtual void Cancel() => Closed = true;

    /// <summary>Gets the hardware cursor position (null hides it).</summary>
    public virtual (int x, int y)? Cursor => null;

    /// <summary>Strips `accent` markup (for console --help).</summary>
    internal static string StripSpans(string s) => s.Replace("`", "");

    /// <summary>Measures the visible length of a string with spans.</summary>
    internal static int SpanWidth(string s)
    {
        int n = 0;
        foreach (char c in s)
            if (c != '`')
                n++;
        return n;
    }

    /// <summary>Writes a string with `accented` chunks (like bold keys in opencode footers); writes up to maxWidth visible symbols.</summary>
    internal static void WriteSpans(Screen screen, int x, int y, string text, Rgb fg, Rgb accentFg, Rgb bg, int maxWidth)
    {
        int cx = x, left = maxWidth;
        bool accent = false;
        foreach (char c in text)
        {
            if (c == '`')
            {
                accent = !accent;
                continue;
            }
            if (left <= 0)
                break;
            screen.Set(cx++, y, c, accent ? accentFg : fg, bg);
            left--;
        }
    }

    /// <summary>Computes the window top: below center (a quarter of the screen), but not above center for tall windows.</summary>
    internal static int TopY(int screenH, int boxH) =>
        Math.Max(0, Math.Min((screenH - boxH) / 2, screenH / 4));

    /// <summary>Measures the options window: labels on the left, values in an aligned column.</summary>
    internal static DialogBox? MeasureOptions(int screenW, int screenH, string title, string[] labels, string[] values)
    {
        if (screenW < 20 || screenH < 5)
            return null;
        int labelW = 0, valW = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            labelW = Math.Max(labelW, labels[i].Length);
            valW = Math.Max(valW, values[i].Length);
        }
        int inner = 1 + 2 + labelW + 2 + (valW + 4) + 1;
        int boxW = Math.Min(Math.Max(inner + 2, title.Length + 6), screenW);
        int boxH = labels.Length + 2;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = TopY(screenH, boxH);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    /// <summary>
    /// Draws option rows: a ● marker on the selected row, values in one column with the accent color.
    /// Switchable values render in arrows (&lt; &gt;), plain values render as bare text (commands without options).
    /// </summary>
    internal static void DrawOptionRows(Screen screen, Theme theme, DialogBox box,
        string[] labels, string[] values, int selected, bool[]? plain = null)
    {
        int labelW = 0;
        foreach (string l in labels)
            labelW = Math.Max(labelW, l.Length);
        int inner = box.W - 2;
        for (int i = 0; i < labels.Length; i++)
        {
            bool sel = i == selected;
            var (fg, bg) = sel
                ? (theme.ButtonSelFg, theme.ButtonSelBg)
                : (theme.ModalFg, theme.ModalBg);
            string val = FormatOptionValue(values[i], plain is not null && i < plain.Length && plain[i]);
            string cell = " " + (sel ? "● " : "  ") + labels[i].PadRight(labelW) + "  " + val;
            if (cell.Length > inner)
                cell = cell[..inner];
            cell = cell.PadRight(inner);
            int y = box.Y0 + 1 + i;
            screen.Text(box.X0, y, "│", fg, bg);
            // Renders the value in an aligned column with accent (inverted on the selected row).
            // Offset: " "(1) + marker(2) + label + "  "(2) - exactly the start of val.
            int valX = 1 + 2 + labelW + 2;
            for (int k = 0; k < cell.Length && k < inner; k++)
            {
                bool isVal = k >= valX && k < valX + val.Length;
                screen.Set(box.X0 + 1 + k, y, cell[k],
                    sel ? fg : isVal ? theme.AccentFg : fg, bg);
            }
            screen.Text(box.X0 + box.W - 1, y, "│", fg, bg);
        }
    }

    /// <summary>Formats an option row value: switchable values get arrows, plain/empty values stay as-is.</summary>
    internal static string FormatOptionValue(string value, bool plain) =>
        plain || value.Length == 0 ? value : $"< {value} >";

    /// <summary>Centers text (truncates to the width).</summary>
    internal static string CenterPad(string s, int width)
    {
        if (s.Length >= width)
            return s[..Math.Max(0, width)];
        int left = (width - s.Length) / 2;
        return new string(' ', left) + s + new string(' ', width - s.Length - left);
    }

    /// <summary>Fits text into exactly <paramref name="width"/> cells (pads or truncates; never throws).</summary>
    internal static string FitCell(string s, int width) =>
        width <= 0 ? string.Empty : s.PadRight(width)[..width];

    internal static string MiddleTruncate(string s, int width)
    {
        if (s.Length <= width)
            return s;
        if (width <= 6)
            return s[..Math.Max(0, width)];
        int head = (width - 3) / 2;
        return s[..head] + "..." + s[^(width - 3 - head)..];
    }
}
