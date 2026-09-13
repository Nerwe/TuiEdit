namespace TuiEdit;

/// <summary>Pure prompt Tab-completion helpers (no console access).</summary>
internal static class PromptComplete
{
    /// <summary>Command-line verbs for the first token.</summary>
    public static readonly string[] CmdlineVerbs =
        ["earlier", "exit", "find", "go", "goto", "later", "q", "quit", "save", "set", "w"];

    /// <summary>Setting keys for <c>set</c>.</summary>
    public static readonly string[] SetKeys =
    [
        "backup", "copyselect", "enc", "encoding", "ending", "eol", "guides", "gutter",
        "indent", "lang", "language", "mouse", "numbers", "pairs", "ruler", "session",
        "theme", "whitespace", "wrap",
    ];

    /// <summary>On/off values for bool settings.</summary>
    public static readonly string[] BoolValues = ["off", "on"];

    /// <summary>Completes the command line at pos (verbs, set keys, bool values); cycles on repeat.</summary>
    public static (string Text, int Pos) CompleteCmdline(string text, int pos)
    {
        string[] tokens = text[..Math.Clamp(pos, 0, text.Length)].Split(' ');
        if (tokens.Length <= 1)
            return ApplyToken(text, pos, CmdlineVerbs, TokenStart(text, pos, char.IsWhiteSpace));
        if (tokens[0].Equals("set", StringComparison.OrdinalIgnoreCase) && tokens.Length == 2)
            return ApplyToken(text, pos, SetKeys, TokenStart(text, pos, char.IsWhiteSpace));
        if (tokens[0].Equals("set", StringComparison.OrdinalIgnoreCase) && tokens.Length == 3)
            return ApplyToken(text, pos, BoolValues, TokenStart(text, pos, char.IsWhiteSpace));
        return (text, pos);
    }

    /// <summary>Completes a buffer word at pos from candidates; cycles on repeat.</summary>
    public static (string Text, int Pos) CompleteWord(string text, int pos, IReadOnlyList<string> candidates)
    {
        int start = pos;
        while (start > 0 && TextBuffer.IsWordChar(text[start - 1]))
            start--;
        return ApplyToken(text, pos, candidates, start);
    }

    private static int TokenStart(string text, int pos, Func<char, bool> isBoundary)
    {
        int start = Math.Clamp(pos, 0, text.Length);
        while (start > 0 && !isBoundary(text[start - 1]))
            start--;
        return start;
    }

    private static (string Text, int Pos) ApplyToken(
        string text, int pos, IReadOnlyList<string> candidates, int start)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        start = Math.Clamp(start, 0, pos);
        string prefix = text[start..pos];
        List<string> matches = [];
        foreach (string c in candidates)
        {
            if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                matches.Add(c);
        }
        if (matches.Count == 0)
            return (text, pos);
        matches.Sort(StringComparer.OrdinalIgnoreCase);
        string pick;
        if (matches.FindIndex(c => c.Equals(prefix, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Cycle: the token is already complete, so advance through the full list.
            List<string> all = new(candidates);
            all.Sort(StringComparer.OrdinalIgnoreCase);
            int at = all.FindIndex(c => c.Equals(prefix, StringComparison.OrdinalIgnoreCase));
            pick = all[(at + 1) % all.Count];
        }
        else
        {
            pick = matches[0];
        }
        return (text[..start] + pick + text[pos..], start + pick.Length);
    }
}
