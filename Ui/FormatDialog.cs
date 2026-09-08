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
        var (labels, values) = Rows(loc);
        return MeasureOptions(screenW, screenH, GetTitle(loc), labels, values);
    }

    private (string[] labels, string[] values) Rows(Loc loc) =>
        ([loc["format.encoding"], loc["format.endings"], loc["format.indent"]],
         [_buf.EncodingLabel, _buf.EndingLabel, _buf.IndentLabel]);

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        var (labels, values) = Rows(loc);
        DrawOptionRows(screen, theme, box, labels, values, _row);
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

    /// <summary>Клик по строке: выбрать и шагнуть (+1), как стрелка вправо.</summary>
    public override bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return false;
        DialogBox b = box.Value;
        if (x < b.X0 || x >= b.X0 + b.W || y < b.Y0 || y >= b.Y0 + b.H)
            return false;
        int row = y - (b.Y0 + 1); // строки опций — зеркало DrawOptionRows
        if (row < 0 || row >= RowCount)
            return true;
        _row = row;
        Cycle(1);
        return true;
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
