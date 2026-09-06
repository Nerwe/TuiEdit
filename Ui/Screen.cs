namespace TuiEdit;

/// <summary>
/// Кадр экрана: ячейки (символ + цвета) с diff-выводом.
/// Идея как framebuffer.rs в MS Edit: каждый кадр рисуется в память,
/// а в консоль уходят только изменившиеся runs — поэтому нет мигания.
/// Чистая модель (ComputeDiff) — покрывается unit-тестами.
/// </summary>
public sealed class Screen
{
    /// <summary>Ячейка экрана.</summary>
    public readonly record struct Cell(char Ch, Rgb Fg, Rgb Bg);

    /// <summary>Операция вывода: непрерывный run одного цвета.</summary>
    public readonly record struct DrawOp(int X, int Y, string Text, Rgb Fg, Rgb Bg);

    /// <summary>Truecolor-ANSI вывод (false — ближайшие 16 цветов консоли).</summary>
    public bool TrueColor { get; set; } = true;

    private Cell[,] _cur = new Cell[0, 0];
    private Cell[,] _prev = new Cell[0, 0];

    /// <summary>Ширина экрана.</summary>
    public int Width { get; private set; }

    /// <summary>Высота экрана.</summary>
    public int Height { get; private set; }

    /// <summary>Смена размера (предыдущий кадр сбрасывается — первый вывод полный).</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cur = new Cell[Width, Height];
        _prev = new Cell[Width, Height]; // '\0' — такого символа мы не пишем, diff будет полным
    }

    /// <summary>Поставить ячейку (вне экрана — игнорируется).</summary>
    public void Set(int x, int y, char ch, Rgb fg, Rgb bg)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            _cur[x, y] = new Cell(ch, fg, bg);
    }

    /// <summary>Написать строку с одного цвета.</summary>
    public void Text(int x, int y, string text, Rgb fg, Rgb bg)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int i = 0; i < text.Length; i++)
            Set(x + i, y, text[i], fg, bg);
    }

    /// <summary>Залить run одним символом.</summary>
    public void Fill(int x, int y, int count, char ch, Rgb fg, Rgb bg)
    {
        for (int i = 0; i < count; i++)
            Set(x + i, y, ch, fg, bg);
    }

    /// <summary>
    /// Верхняя рамка модалки с заголовком ровно шириной <paramref name="boxW"/>:
    /// <c>┌─ Title ───┐</c>. Единая формула для всех попапов
    /// (раньше менеджер/настройки были короче на символ).
    /// </summary>
    public static string TitleRow(string title, int boxW)
    {
        string seg = $" {title} ";
        if (seg.Length > boxW - 3)
            seg = seg[..Math.Max(0, boxW - 3)];
        return "┌─" + seg + new string('─', Math.Max(0, boxW - 3 - seg.Length)) + "┐";
    }

    /// <summary>
    /// Разница с предыдущим кадром runsами: подряд идущие изменившиеся
    /// ячейки одного цвета объединяются в одну операцию.
    /// </summary>
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

    /// <summary>Зафиксировать кадр как «предыдущий» (копия — текущий остаётся для инкремента).</summary>
    public void Swap() => _prev = (Cell[,])_cur.Clone();

    /// <summary>Вывести diff в консоль одним проходом (ANSI или 16 цветов).</summary>
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
            // Окно успели изменить между кадром и выводом — следующий кадр поправит.
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
