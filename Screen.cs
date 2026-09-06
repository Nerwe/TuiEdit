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
    public readonly record struct Cell(char Ch, ConsoleColor Fg, ConsoleColor Bg);

    /// <summary>Операция вывода: непрерывный run одного цвета.</summary>
    public readonly record struct DrawOp(int X, int Y, string Text, ConsoleColor Fg, ConsoleColor Bg);

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
    public void Set(int x, int y, char ch, ConsoleColor fg, ConsoleColor bg)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            _cur[x, y] = new Cell(ch, fg, bg);
    }

    /// <summary>Написать строку с одного цвета.</summary>
    public void Text(int x, int y, string text, ConsoleColor fg, ConsoleColor bg)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int i = 0; i < text.Length; i++)
            Set(x + i, y, text[i], fg, bg);
    }

    /// <summary>Залить run одним символом.</summary>
    public void Fill(int x, int y, int count, char ch, ConsoleColor fg, ConsoleColor bg)
    {
        for (int i = 0; i < count; i++)
            Set(x + i, y, ch, fg, bg);
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

    /// <summary>Вывести diff в консоль одним проходом.</summary>
    public void Flush()
    {
        List<DrawOp> ops = ComputeDiff();
        if (ops.Count == 0)
        {
            Swap();
            return;
        }
        ConsoleColor lastFg = (ConsoleColor)(-1);
        ConsoleColor lastBg = (ConsoleColor)(-1);
        try
        {
            foreach (DrawOp op in ops)
            {
                Console.SetCursorPosition(op.X, op.Y);
                if (op.Fg != lastFg)
                {
                    Console.ForegroundColor = op.Fg;
                    lastFg = op.Fg;
                }
                if (op.Bg != lastBg)
                {
                    Console.BackgroundColor = op.Bg;
                    lastBg = op.Bg;
                }
                Console.Write(op.Text);
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException)
        {
            // Окно успели изменить между кадром и выводом — следующий кадр поправит.
        }
        Swap();
    }
}
