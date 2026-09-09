namespace TuiEdit;

/// <summary>
/// Автопары скобок и кавычек: чистая логика решений (применение — в буфере/редакторе).
/// </summary>
public static class AutoPair
{
    /// <summary>Парная закрывающая (или '\0' — не пара).</summary>
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
    /// Вводить пару (вставить оба, курсор между)? Кавычки — только если дальше
    /// не буква/цифра (иначе `don't` превратится в `don''t`).
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
    /// Напечатан закрывающий, а он уже стоит — перепрыгнуть (не вставлять)?
    /// Работает для `)]}`, кавычки — в обе стороны.
    /// </summary>
    public static bool ShouldSkip(string line, int col, char c) =>
        col >= 0 && col < line.Length && line[col] == c
        && (CloserFor(c) == '\0' || IsQuote(c));

    /// <summary>
    /// Backspace между парой (`(|)`) — стереть обе? Возвращает новую колонку.
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
