namespace TuiEdit;

/// <summary>
/// Converts character columns to visual columns: expands tabs to the next stop
/// (tab width is <see cref="Width"/>), other characters are width 1.
/// Buffers store lines as-is (with tabs); expansion applies to rendering only.
/// </summary>
internal static class TabStops
{
    public const int Width = 4;

    /// <summary>Gets the visual width of the first characters of a line.</summary>
    public static int VisualWidth(string line, int charCount)
    {
        ArgumentNullException.ThrowIfNull(line);
        int pos = 0;
        int n = Math.Clamp(charCount, 0, line.Length);
        for (int i = 0; i < n; i++)
            pos += line[i] == '\t' ? Width - pos % Width : 1;
        return pos;
    }

    /// <summary>Finds the character index for a visual column (for cursor positioning).</summary>
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

    /// <summary>Slices a line by visual columns (renders tabs as spaces).</summary>
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
            {
                if (c == '\t')
                    sb.Append(' ', e - s);
                else
                    sb.Append(c);
            }
            pos += w;
            if (pos >= end)
                break;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Wraps lines softly: a tab that does not fit in the segment remainder moves entirely
/// to the next segment — segment boundaries always align with character starts.
/// </summary>
internal static class WordWrap
{
    /// <summary>Gets segment starts in visual columns (the first is always 0).</summary>
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

    public static int SegmentCount(string line, int width) => CountSegments(line, width);

    /// <summary>Counts segments like <see cref="SegmentStarts"/> but without allocating.</summary>
    public static int CountSegments(string line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (width < 1) width = 1;
        int count = 1;
        int lastStart = 0;
        int vpos = 0;
        foreach (char c in line)
        {
            int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
            if (vpos > lastStart && vpos + cw - lastStart > width)
            {
                lastStart = vpos;
                count++;
            }
            vpos += cw;
        }
        return count;
    }

    public static int SegmentAt(List<int> starts, int vcol)
    {
        ArgumentNullException.ThrowIfNull(starts);
        int seg = 0;
        while (seg + 1 < starts.Count && starts[seg + 1] <= vcol)
            seg++;
        return seg;
    }
}
