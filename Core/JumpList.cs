namespace TuiEdit;

/// <summary>A jump position: file (null = untitled) plus cursor. No remapping through edits (v1).</summary>
internal sealed record JumpPosition(string? File, int Row, int Col);

/// <summary>Capped back/forward jump history (Helix-style): push before big jumps,
/// step back to return, forward to redo. Consecutive duplicates drop, a new push
/// truncates the forward branch, oldest entries fall off past capacity.</summary>
internal sealed class JumpList
{
    /// <summary>Max remembered jumps (oldest drop off).</summary>
    internal const int Capacity = 30;

    private readonly List<JumpPosition> _jumps = new();
    private int _index = -1; // current position; -1 = empty

    public int Count => _jumps.Count;

    /// <summary>Records a position (usually the spot before jumping away).</summary>
    public void Push(JumpPosition pos)
    {
        if (_index >= 0 && _index < _jumps.Count && _jumps[_index].Equals(pos))
            return; // consecutive duplicate
        if (_index < _jumps.Count - 1)
            _jumps.RemoveRange(_index + 1, _jumps.Count - _index - 1);
        _jumps.Add(pos);
        if (_jumps.Count > Capacity)
            _jumps.RemoveAt(0);
        _index = _jumps.Count - 1;
    }

    /// <summary>
    /// Steps back, saving the current spot when at the tip (so forward can return).
    /// Returns null when empty or already at the oldest entry.
    /// </summary>
    public JumpPosition? Back(JumpPosition current)
    {
        if (_jumps.Count == 0)
            return null;
        if (_index >= _jumps.Count - 1 && !_jumps[_index].Equals(current))
            Push(current);
        if (_index <= 0)
            return null;
        _index--;
        return _jumps[_index];
    }

    /// <summary>Steps forward. Returns null when empty or already at the tip.</summary>
    public JumpPosition? Forward()
    {
        if (_index >= _jumps.Count - 1)
            return null;
        _index++;
        return _jumps[_index];
    }
}
