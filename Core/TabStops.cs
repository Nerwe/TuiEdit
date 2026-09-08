namespace TuiEdit;

/// <summary>
/// Пересчёт символьных колонок в визуальные: табуляция раскрывается
/// до следующей стоп-позиции (как в MS Edit), остальные символы — шириной 1.
/// Строки в буфере хранятся как есть (с табами), раскрытие — только для отрисовки.
/// </summary>
public static class TabStops
{
    public const int Width = 4;

    /// <summary>Визуальная ширина первых символов строки.</summary>
    public static int VisualWidth(string line, int charCount)
    {
        ArgumentNullException.ThrowIfNull(line);
        int pos = 0;
        int n = Math.Clamp(charCount, 0, line.Length);
        for (int i = 0; i < n; i++)
            pos += line[i] == '\t' ? Width - pos % Width : 1;
        return pos;
    }

    /// <summary>Индекс символа по визуальной колонке (для позиционирования курсора).</summary>
    public static int CharIndexAtVisual(string line, int visualCol)
    {
        ArgumentNullException.ThrowIfNull(line);
        int pos = 0;
        int i = 0;
        while (i < line.Length && pos < visualCol)
        {
            pos += line[i] == '\t' ? Width - pos % Width : 1;
            i++;
        }
        return i;
    }

    /// <summary>Срез строки по визуальным колонкам (табы — пробелами).</summary>
    public static string Slice(string line, int startVisual, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (maxWidth <= 0 || startVisual < 0)
            return string.Empty;
        var sb = new System.Text.StringBuilder(Math.Min(maxWidth, 64));
        int pos = 0;
        int end = startVisual + maxWidth;
        foreach (char c in line)
        {
            int w = c == '\t' ? Width - pos % Width : 1;
            int s = Math.Max(pos, startVisual);
            int e = Math.Min(pos + w, end);
            if (e > s)
                sb.Append(c == '\t' ? new string(' ', e - s) : c.ToString());
            pos += w;
            if (pos >= end)
                break;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Мягкий перенос строк: таб, не влезающий в остаток сегмента, целиком едет
/// на следующий — границы сегментов всегда совпадают с началами символов.
/// </summary>
public static class WordWrap
{
    /// <summary>Старты сегментов в визуальных колонках (первый всегда 0).</summary>
    public static List<int> SegmentStarts(string line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (width < 1) width = 1;
        var starts = new List<int> { 0 };
        int vpos = 0;
        foreach (char c in line)
        {
            int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
            if (vpos > starts[^1] && vpos + cw - starts[^1] > width)
                starts.Add(vpos);
            vpos += cw;
        }
        return starts;
    }

    public static int SegmentCount(string line, int width) => SegmentStarts(line, width).Count;

    public static int SegmentAt(List<int> starts, int vcol)
    {
        ArgumentNullException.ThrowIfNull(starts);
        int seg = 0;
        while (seg + 1 < starts.Count && starts[seg + 1] <= vcol)
            seg++;
        return seg;
    }
}
