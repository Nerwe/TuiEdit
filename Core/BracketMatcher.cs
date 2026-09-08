namespace TuiEdit;

internal static class BracketMatcher
{
    private const int MaxScan = 100_000;

    private static readonly Dictionary<char, char> OpenToClose = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
    };

    private static readonly Dictionary<char, char> CloseToOpen = new()
    {
        [')'] = '(',
        [']'] = '[',
        ['}'] = '{',
    };

    public static (int Row, int Col)? FindMatch(TextBuffer buf, SyntaxHighlighter hl,
        CompiledGrammar? grammar, int row, int col)
    {
        if (row < 0 || row >= buf.Count)
            return null;
        string line = buf.GetLine(row);
        int at = -1;
        bool forward = true;
        if (col >= 0 && col < line.Length && IsBracket(line[col]))
        {
            at = col;
            forward = OpenToClose.ContainsKey(line[col]);
        }
        else if (col > 0 && col <= line.Length && IsBracket(line[col - 1]))
        {
            at = col - 1;
            forward = OpenToClose.ContainsKey(line[col - 1]);
        }
        if (at < 0)
            return null;
        if (!IsCode(hl.GetLine(buf, grammar, row), at))
            return null;
        return forward
            ? ScanForward(buf, hl, grammar, row, at)
            : ScanBackward(buf, hl, grammar, row, at);
    }

    internal static bool IsBracket(char c) => OpenToClose.ContainsKey(c) || CloseToOpen.ContainsKey(c);

    private static bool IsCode(IReadOnlyList<SyntaxToken> toks, int col)
    {
        foreach (var t in toks)
            if (col >= t.Start && col < t.Start + t.Length)
                return t.Scope is not "string" and not "comment";
        return true;
    }

    private static (int Row, int Col)? ScanForward(TextBuffer buf, SyntaxHighlighter hl,
        CompiledGrammar? grammar, int row, int col)
    {
        char open = buf.GetLine(row)[col];
        char close = OpenToClose[open];
        int depth = 0;
        int scanned = 0;
        for (int r = row; r < buf.Count; r++)
        {
            string line = buf.GetLine(r);
            var toks = hl.GetLine(buf, grammar, r);
            for (int c = r == row ? col : 0; c < line.Length; c++)
            {
                if (++scanned > MaxScan)
                    return null;
                char ch = line[c];
                if (ch != open && ch != close)
                    continue;
                if (!IsCode(toks, c))
                    continue;
                if (ch == open)
                    depth++;
                else if (--depth == 0)
                    return (r, c);
            }
        }
        return null;
    }

    private static (int Row, int Col)? ScanBackward(TextBuffer buf, SyntaxHighlighter hl,
        CompiledGrammar? grammar, int row, int col)
    {
        char close = buf.GetLine(row)[col];
        char open = CloseToOpen[close];
        int depth = 0;
        int scanned = 0;
        for (int r = row; r >= 0; r--)
        {
            string line = buf.GetLine(r);
            var toks = hl.GetLine(buf, grammar, r);
            for (int c = r == row ? col : line.Length - 1; c >= 0; c--)
            {
                if (++scanned > MaxScan)
                    return null;
                char ch = line[c];
                if (ch != open && ch != close)
                    continue;
                if (!IsCode(toks, c))
                    continue;
                if (ch == close)
                    depth++;
                else if (--depth == 0)
                    return (r, c);
            }
        }
        return null;
    }
}
