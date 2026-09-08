namespace TuiEdit;

/// <summary>Формат файла: кодировка, переводы строк и отступ; ←/→ листают, применяется сразу.</summary>
internal sealed class FormatDialog : Dialog
{
    internal const int RowCount = 3;

    private readonly TextBuffer _buf;
    private int _row;

    public FormatDialog(TextBuffer buf)
    {
        _buf = buf;
    }

    protected override string GetTitle(Loc loc) => loc["format.title"];

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        if (screenW < 20 || screenH < 5)
            return null;
        var (labels, values) = Rows(loc);
        int inner = 0;
        for (int i = 0; i < RowCount; i++)
            inner = Math.Max(inner, labels[i].Length + values[i].Length + 8);
        string title = GetTitle(loc);
        int boxW = Math.Min(Math.Max(inner + 2, title.Length + 6), screenW);
        int boxH = RowCount + 2;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = Math.Max(0, (screenH - boxH) / 2);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    private (string[] labels, string[] values) Rows(Loc loc) =>
        ([loc["format.encoding"], loc["format.endings"], loc["format.indent"]],
         [_buf.EncodingLabel, _buf.EndingLabel, _buf.IndentLabel]);

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        var (labels, values) = Rows(loc);
        Theme t = theme;
        for (int i = 0; i < RowCount; i++)
        {
            string cell = $" {labels[i]}: < {values[i]} >";
            int inner = box.W - 2;
            if (cell.Length > inner)
                cell = cell[..inner];
            int y = box.Y0 + 1 + i;
            if (i == _row)
                screen.Text(box.X0, y, "│" + cell.PadRight(inner) + "│", t.ButtonSelFg, t.ButtonSelBg);
            else
                screen.Text(box.X0, y, "│" + cell.PadRight(inner) + "│", t.ModalFg, t.ModalBg);
        }
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        var k = key;
        if ((k.Modifiers & ConsoleModifiers.Control) != 0)
            return;
        switch (k.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
                Closed = true;
                return;
            case ConsoleKey.UpArrow: _row = Math.Max(0, _row - 1); break;
            case ConsoleKey.DownArrow: _row = Math.Min(RowCount - 1, _row + 1); break;
            case ConsoleKey.LeftArrow: Cycle(-1); break;
            case ConsoleKey.RightArrow: Cycle(1); break;
        }
    }

    private void Cycle(int dir)
    {
        switch (_row)
        {
            case 0: _buf.CycleEncoding(dir); break;
            case 1: _buf.CycleEnding(dir); break;
            default: _buf.CycleIndent(dir); break;
        }
    }
}
