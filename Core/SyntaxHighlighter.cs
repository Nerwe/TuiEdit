using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Represents a highlighting span: position, length, and scope.</summary>
internal readonly record struct SyntaxToken(int Start, int Length, string Scope);

/// <summary>
/// Highlights via JSON grammars: matches and multiline begin/end.
/// Caches line states and recalculates from the first modified line.
/// </summary>
internal sealed class SyntaxHighlighter
{
    private CompiledGrammar? _grammar;
    private int _version = -1;
    private readonly List<List<SyntaxToken>> _lines = new();
    private readonly List<string> _stacks = new();
    private readonly List<string> _texts = new();

    public IReadOnlyList<SyntaxToken> GetLine(TextBuffer buf, CompiledGrammar? grammar, int row)
    {
        if (grammar is null || row < 0 || row >= buf.Count)
            return [];
        Ensure(buf, grammar);
        return row < _lines.Count ? _lines[row] : [];
    }

    private void Ensure(TextBuffer buf, CompiledGrammar grammar)
    {
        if (!ReferenceEquals(grammar, _grammar))
        {
            _grammar = grammar;
            _version = -1;
            _lines.Clear();
            _stacks.Clear();
            _texts.Clear();
        }
        if (buf.Version == _version && buf.Count == _lines.Count)
            return;
        int oldCount = _texts.Count;
        int delta = buf.Count - oldCount; // Pure line shift (insert/delete)
        int n = Math.Min(buf.Count, oldCount);
        int dirty = 0;
        while (dirty < n && ReferenceEquals(buf.Lines[dirty], _texts[dirty]))
            dirty++;
        // Keeps the old tail for early exit while truncating the lists.
        List<string> tailTexts = _texts.GetRange(dirty, oldCount - dirty);
        List<string> tailStacks = _stacks.GetRange(dirty, oldCount - dirty);
        List<List<SyntaxToken>> tailLines = _lines.GetRange(dirty, oldCount - dirty);
        if (_lines.Count > dirty)
        {
            _lines.RemoveRange(dirty, _lines.Count - dirty);
            _stacks.RemoveRange(dirty, _stacks.Count - dirty);
            _texts.RemoveRange(dirty, _texts.Count - dirty);
        }
        var stack = new Stack<(string Scope, Regex End)>();
        if (dirty > 0)
            PushStack(stack, grammar, _stacks[dirty - 1]);
        for (int i = dirty; i < buf.Count; i++)
        {
            string line = buf.Lines[i];
            List<SyntaxToken> tokens = TokenizeLine(line, stack, grammar.Rules);
            string sig = StackSignature(stack);
            _lines.Add(tokens);
            _stacks.Add(sig);
            _texts.Add(line);
            // Early exit: text and outgoing stack match the old cache, so state has converged.
            // Reuses the tail only when the ENTIRE remainder matches line by line
            // (guards against multiple edits in one version).
            int t = i - dirty - delta;
            if (t < 0 || t >= tailTexts.Count)
                continue;
            if (!ReferenceEquals(line, tailTexts[t]) || sig != tailStacks[t])
                continue;
            if (!TailMatches(buf, tailTexts, i + 1, dirty, delta))
                continue;
            for (int k = t + 1; k < tailTexts.Count; k++)
            {
                _lines.Add(tailLines[k]);
                _stacks.Add(tailStacks[k]);
                _texts.Add(tailTexts[k]);
            }
            break;
        }
        _version = buf.Version;
    }

    private static string StackSignature(Stack<(string Scope, Regex End)> stack) =>
        string.Join("\u001F", stack.Reverse().Select(e => e.Scope));

    /// <summary>Determines whether the entire remaining buffer matches the old tail (by reference, with shift).</summary>
    private static bool TailMatches(TextBuffer buf, List<string> tailTexts, int fromNew, int dirty, int delta)
    {
        for (int j = fromNew; j < buf.Count; j++)
        {
            int t = j - dirty - delta;
            if ((uint)t >= (uint)tailTexts.Count || !ReferenceEquals(buf.Lines[j], tailTexts[t]))
                return false;
        }
        return true;
    }

    private static void PushStack(Stack<(string Scope, Regex End)> stack, CompiledGrammar grammar, string sig)
    {
        foreach (string scope in sig.Split('\u001F', StringSplitOptions.RemoveEmptyEntries))
        {
            Regex? end = grammar.Rules.FirstOrDefault(r => r.IsMultiline && r.Scope == scope)?.End;
            if (end is not null)
                stack.Push((scope, end));
        }
    }

    /// <summary>Tokenizes a line with the incoming stack (mutates the stack to the outgoing state).</summary>
    internal static List<SyntaxToken> TokenizeLine(
        string line, Stack<(string Scope, Regex End)> stack, List<CompiledRule> rules)
    {
        var tokens = new List<SyntaxToken>();
        string Cur() => stack.Count > 0 ? stack.Peek().Scope : string.Empty;
        int pos = 0;
        int guard = 0;
        while (pos <= line.Length && guard++ < line.Length * 2 + 10)
        {
            int bestIndex = int.MaxValue;
            Match? bestMatch = null;
            CompiledRule? bestRule = null;
            bool bestIsEnd = false;
            if (stack.Count > 0)
            {
                Match m;
                try
                {
                    m = stack.Peek().End.Match(line, pos);
                }
                catch (RegexMatchTimeoutException)
                {
                    return GapOnly(line);
                }
                if (m.Success && m.Index < bestIndex)
                {
                    bestIndex = m.Index;
                    bestMatch = m;
                    bestRule = null;
                    bestIsEnd = true;
                }
            }
            foreach (CompiledRule r in rules)
            {
                if (stack.Count > 0)
                    break;
                Match? m = TryMatch(r.Match, line, pos) ?? TryMatch(r.Begin, line, pos);
                if (m is null)
                    continue;
                bool isBegin = r.Match is null;
                if (m.Index < bestIndex)
                {
                    bestIndex = m.Index;
                    bestMatch = m;
                    bestRule = r;
                    bestIsEnd = false;
                    if (isBegin && m.Index == pos)
                        break;
                }
            }
            if (bestMatch is null)
                break;
            if (bestIndex > pos)
            {
                tokens.Add(new SyntaxToken(pos, bestIndex - pos, Cur()));
                pos = bestIndex;
            }
            if (bestIsEnd)
            {
                string scope = stack.Pop().Scope;
                int len = Math.Max(0, bestMatch.Index + bestMatch.Length - pos);
                if (len > 0)
                    tokens.Add(new SyntaxToken(pos, len, scope));
                pos = bestMatch.Index + bestMatch.Length;
                if (pos <= bestIndex)
                    pos = bestIndex + 1;
                continue;
            }
            bool begin = bestRule!.Match is null;
            int endPos = bestMatch.Index + bestMatch.Length;
            if (!begin)
            {
                int len = Math.Max(endPos - pos, 0);
                if (len > 0)
                    tokens.Add(new SyntaxToken(pos, len, bestRule.Scope));
                pos = endPos <= pos ? pos + 1 : endPos;
                continue;
            }
            Match? endM = TryMatch(bestRule.End, line, endPos);
            if (endM is null)
            {
                if (line.Length - pos > 0)
                    tokens.Add(new SyntaxToken(pos, line.Length - pos, bestRule.Scope));
                stack.Push((bestRule.Scope, bestRule.End!));
                break;
            }
            int full = endM.Index + endM.Length - pos;
            if (full > 0)
                tokens.Add(new SyntaxToken(pos, full, bestRule.Scope));
            pos = endM.Index + endM.Length <= pos ? pos + 1 : endM.Index + endM.Length;
        }
        if (pos < line.Length)
            tokens.Add(new SyntaxToken(pos, line.Length - pos, Cur()));
        else if (tokens.Count == 0)
            tokens.Add(new SyntaxToken(0, 0, Cur()));
        return tokens;
    }

    private static Match? TryMatch(Regex? rx, string line, int pos)
    {
        if (rx is null)
            return null;
        try
        {
            Match m = rx.Match(line, pos);
            return m.Success ? m : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static List<SyntaxToken> GapOnly(string line) =>
        line.Length == 0
            ? new List<SyntaxToken> { new SyntaxToken(0, 0, string.Empty) }
            : new List<SyntaxToken> { new SyntaxToken(0, line.Length, string.Empty) };

    /// <summary>Gets the theme color for a scope (null means plain text).</summary>
    internal static Rgb? ScopeColor(Theme theme, string scope) => scope switch
    {
        "keyword" => theme.SynKeywordFg,
        "string" => theme.SynStringFg,
        "comment" => theme.SynCommentFg,
        "number" => theme.SynNumberFg,
        "type" => theme.SynTypeFg,
        _ => null,
    };
}
