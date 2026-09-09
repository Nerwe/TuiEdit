namespace TuiEdit;

internal enum CharClass
{
    Whitespace,
    Separator,
    Word,
}

/// <summary>
/// Provides VS Code-style word navigation:
/// skips whitespace, then one word or separator
/// (after a single separator — also the next word).
/// </summary>
internal static class WordMotion
{
    private const string Separators = "`~!@#$%^&*()-=+[{]}\\|;:'\",.<>/?";

    public static CharClass Classify(char c) => c switch
    {
        ' ' or '\t' => CharClass.Whitespace,
        var ch when Separators.Contains(ch) => CharClass.Separator,
        _ => CharClass.Word,
    };

    /// <summary>Finds the next word boundary forward (like <c>word_forward</c>).</summary>
    public static int Forward(string line, int col)
    {
        ArgumentNullException.ThrowIfNull(line);
        int i = Math.Clamp(col, 0, line.Length);
        while (i < line.Length && Classify(line[i]) == CharClass.Whitespace)
            i++;
        if (i < line.Length)
        {
            CharClass cls = Classify(line[i]);
            int start = i;
            i++;
            while (i < line.Length && Classify(line[i]) == cls)
                i++;
            if (i == start + 1 && cls == CharClass.Separator)
                while (i < line.Length && Classify(line[i]) == CharClass.Word)
                    i++;
        }
        return i;
    }

    /// <summary>Finds the previous word boundary (like <c>word_backward</c>).</summary>
    public static int Backward(string line, int col)
    {
        ArgumentNullException.ThrowIfNull(line);
        int i = Math.Clamp(col, 0, line.Length);
        while (i > 0 && Classify(line[i - 1]) == CharClass.Whitespace)
            i--;
        if (i > 0)
        {
            CharClass cls = Classify(line[i - 1]);
            int start = i;
            i--;
            while (i > 0 && Classify(line[i - 1]) == cls)
                i--;
            if (i == start - 1 && cls == CharClass.Separator)
                while (i > 0 && Classify(line[i - 1]) == CharClass.Word)
                    i--;
        }
        return i;
    }
}
