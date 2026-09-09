namespace TuiEdit;

/// <summary>
/// Provides natural name comparison (<c>file2</c> before <c>file10</c>):
/// compares digit runs numerically and other text case-insensitively.
/// </summary>
public static class NaturalSort
{
    public static IComparer<string> Comparer { get; } =
        Comparer<string>.Create((a, b) => Compare(a, b));

    public static int Compare(string? a, string? b)
    {
        if (a is null)
            return b is null ? 0 : -1;
        if (b is null)
            return 1;
        int c = CompareCore(a, b);
        // Still case-insensitive — breaks ties deterministically by case.
        return c != 0 ? c : string.Compare(a, b, StringComparison.Ordinal);
    }

    private static int CompareCore(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            bool da = char.IsAsciiDigit(a[i]);
            bool db = char.IsAsciiDigit(b[j]);
            int c;
            if (da && db)
            {
                c = CompareNumbers(a, ref i, b, ref j);
                if (c != 0)
                    return c;
                continue;
            }
            if (da != db)
                return da ? -1 : 1; // Sorts digits before letters (like Explorer)
            int i0 = i, j0 = j;
            while (i < a.Length && !char.IsAsciiDigit(a[i]))
                i++;
            while (j < b.Length && !char.IsAsciiDigit(b[j]))
                j++;
            c = string.Compare(a, i0, b, j0, Math.Min(i - i0, j - j0),
                StringComparison.OrdinalIgnoreCase);
            if (c != 0)
                return c;
            // Shared prefix matches — the loop (run by run) or length decides next.
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Compares numbers of any length: by value first, then by fewer leading zeros on ties.</summary>
    private static int CompareNumbers(string a, ref int i, string b, ref int j)
    {
        int i0 = i, j0 = j;
        while (i < a.Length && char.IsAsciiDigit(a[i]))
            i++;
        while (j < b.Length && char.IsAsciiDigit(b[j]))
            j++;
        // Compares values without leading zeros: longer means larger.
        int ia = i0, ja = j0;
        while (ia + 1 < i && a[ia] == '0')
            ia++;
        while (ja + 1 < j && b[ja] == '0')
            ja++;
        int c = (i - ia).CompareTo(j - ja);
        if (c != 0)
            return c;
        c = string.Compare(a, ia, b, ja, i - ia, StringComparison.Ordinal);
        if (c != 0)
            return c;
        // Values match — shorter notation (fewer zeros) sorts first.
        return (i - i0).CompareTo(j - j0);
    }
}
