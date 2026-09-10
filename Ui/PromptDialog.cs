namespace TuiEdit;

/// <summary>
/// Single-line text prompt dialog (sidebar create/rename): title, input row
/// with selection highlight, hint row. Enter accepts, Esc cancels.
/// </summary>
internal sealed class PromptDialog : Dialog
{
    private readonly string _title;
    private readonly string _hint;
    private readonly Action<string?> _onDone;
    private readonly LineField _field = new();
    private int _cursorX = -1;
    private int _cursorY = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptDialog"/> class.
    /// </summary>
    /// <param name="title">The dialog title (already localized).</param>
    /// <param name="hint">The hint row (already localized).</param>
    /// <param name="initial">The prefilled text.</param>
    /// <param name="onDone">Called with the text on Enter, null on Esc.</param>
    public PromptDialog(string title, string hint, string initial, Action<string?> onDone)
    {
        _title = title;
        _hint = hint;
        _onDone = onDone;
        _field.Set(initial);
        _field.End(select: false);
    }

    /// <summary>Gets the current text (for tests).</summary>
    internal string Text => _field.Text;

    protected override string GetTitle(Loc loc) => _title;

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        int boxW = Math.Min(Math.Max(Math.Max(_title.Length, _hint.Length) + 6, 32), screenW);
        if (boxW < 20 || screenH < 6)
            return null;
        const int boxH = 4; // frame + input + hint + frame
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = TopY(screenH, boxH);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        int x0 = box.X0, y0 = box.Y0, inner = box.W - 2;
        string full = _field.Text;
        int shift = Math.Max(0, full.Length - inner);
        _field.GetSelection(out int selA, out int selB);
        for (int i = 0; i < inner; i++)
        {
            int fi = shift + i;
            char ch = fi < full.Length ? full[fi] : ' ';
            bool sel = fi >= selA && fi < selB;
            screen.Set(x0 + 1 + i, y0 + 1, ch, sel ? theme.SelFg : fg, sel ? theme.SelBg : bg);
        }
        screen.Text(x0, y0 + 1, "│", fg, bg);
        screen.Text(x0 + box.W - 1, y0 + 1, "│", fg, bg);
        int ncx = x0 + 1 + _field.Pos - shift;
        _cursorX = ncx >= x0 + 1 && ncx < x0 + box.W - 1 ? ncx : -1;
        _cursorY = y0 + 1;
        string hint = Dialog.FitCell(_hint, inner);
        screen.Text(x0, y0 + 2, "│" + hint + "│", theme.ModalHintFg, bg);
    }

    /// <inheritdoc/>
    public override (int x, int y)? Cursor => _cursorX >= 0 ? (_cursorX, _cursorY) : null;

    /// <inheritdoc/>
    public override void HandleKey(ConsoleKeyInfo key)
    {
        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
        {
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Backspace)
                _field.DeleteWord(-1);
            return;
        }
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Closed = true;
                _onDone(null);
                return;
            case ConsoleKey.Enter:
                Closed = true;
                _onDone(_field.Text);
                return;
            case ConsoleKey.Backspace: _field.Backspace(); return;
            case ConsoleKey.Delete: _field.DeleteChar(); return;
            case ConsoleKey.LeftArrow: _field.Move(-1, shift); return;
            case ConsoleKey.RightArrow: _field.Move(1, shift); return;
            case ConsoleKey.Home: _field.Home(shift); return;
            case ConsoleKey.End: _field.End(shift); return;
        }
        if (!char.IsControl(key.KeyChar))
            _field.Insert(key.KeyChar.ToString());
    }
}
