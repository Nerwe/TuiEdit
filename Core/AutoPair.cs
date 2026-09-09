namespace TuiEdit;

/// <summary>
/// Provides auto-pairing for brackets and quotes: pure decision logic (applied in the buffer/editor).
/// </summary>
internal static class AutoPair
{
    /// <summary>Gets the matching closer (or '\0' for a non-pair).</summary>
    public static char CloserFor(char c) => c switch
    {
        '(' => ')',
        '[' => ']',
        '{' => '}',
        '"' => '"',
        '\'' => '\'',
        _ => '\0',
    };

    private static bool IsQuote(char c) => c is '"' or '\'';

    /// <summary>
    /// Determines whether to insert a pair (both characters, cursor between)? For quotes, only when not
    /// followed by a letter/digit (otherwise `don't` would become `don''t`).
    /// </summary>
    public static bool ShouldPair(string line, int col, char opener)
    {
        if (CloserFor(opener) == '\0' || col < 0 || col > line.Length)
            return false;
        if (!IsQuote(opener))
            return true;
        return col >= line.Length || !char.IsLetterOrDigit(line[col]);
    }

    /// <summary>
    /// Determines whether a typed closer should skip over an existing one (without inserting)?
    /// Applies to `)]}`, quotes work in both directions.
    /// </summary>
    public static bool ShouldSkip(string line, int col, char c) =>
        col >= 0 && col < line.Length && line[col] == c
        && (CloserFor(c) == '\0' || IsQuote(c));

    /// <summary>
    /// Determines whether Backspace between a pair (`(|)`) deletes both. Returns the new column.
    /// </summary>
    public static int? PairDeleteCol(string line, int col)
    {
        if (col <= 0 || col >= line.Length)
            return null;
        if (CloserFor(line[col - 1]) == line[col])
            return col - 1;
        return null;
    }
}
