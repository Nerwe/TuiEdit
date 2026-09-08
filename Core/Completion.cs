namespace TuiEdit;

/// <summary>Автодополнение из слов буфера (без внешних словарей).</summary>
internal static class Completion
{
    /// <summary>Уникальные слова длиннее префикса: сначала с тем же регистром, затем остальные.</summary>
    public static List<string> Collect(TextBuffer buf, string prefix)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exact = new List<string>();
        var other = new List<string>();
        foreach (string line in buf.Lines)
        {
            int i = 0;
            while (i < line.Length)
            {
                if (!TextBuffer.IsWordChar(line[i]))
                {
                    i++;
                    continue;
                }
                int j = i;
                while (j < line.Length && TextBuffer.IsWordChar(line[j]))
                    j++;
                string w = line[i..j];
                i = j;
                if (w.Length <= prefix.Length || !w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add(w))
                    continue;
                (w.StartsWith(prefix, StringComparison.Ordinal) ? exact : other).Add(w);
            }
        }
        exact.Sort(StringComparer.Ordinal);
        other.Sort(StringComparer.Ordinal);
        exact.AddRange(other);
        return exact;
    }
}
