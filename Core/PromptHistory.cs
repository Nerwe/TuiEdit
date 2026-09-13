namespace TuiEdit;

/// <summary>Per-kind prompt history (most-recent-first, capped, deduped).</summary>
internal sealed class PromptHistory
{
    public const int Cap = 50;

    private readonly Dictionary<string, List<string>> _map = new(StringComparer.Ordinal);

    /// <summary>Pushes text into a kind (skips empty, moves duplicates to front).</summary>
    public void Push(string kind, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (string.IsNullOrEmpty(text))
            return;
        if (!_map.TryGetValue(kind, out List<string>? list))
            _map[kind] = list = [];
        int at = list.FindIndex(s => string.Equals(s, text, StringComparison.Ordinal));
        if (at >= 0)
            list.RemoveAt(at);
        list.Insert(0, text);
        if (list.Count > Cap)
            list.RemoveRange(Cap, list.Count - Cap);
    }

    /// <summary>Gets the history for a kind (most-recent-first, never null).</summary>
    public IReadOnlyList<string> Get(string kind) =>
        _map.TryGetValue(kind, out List<string>? list) ? list : [];
}
