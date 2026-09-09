namespace TuiEdit;

/// <summary>
/// Represents a single-line input field: text, cursor, and selection anchor.
/// Implements a pure model shared by the manager name field and prompts.
/// </summary>
internal sealed class LineField
{
    public string Text { get; private set; } = string.Empty;

    public int Pos { get; private set; }

    public int? Anchor { get; private set; }

    public bool HasSelection => Anchor is int a && a != Pos;

    public void GetSelection(out int a, out int b)
    {
        int anchor = Anchor ?? Pos;
        a = Math.Min(anchor, Pos);
        b = Math.Max(anchor, Pos);
    }

    public void ClearSelection() => Anchor = null;

    public void Set(string text)
    {
        Text = text ?? string.Empty;
        Pos = Math.Clamp(Pos, 0, Text.Length);
        Anchor = null;
    }

    /// <summary>Inserts input over the selection.</summary>
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        DeleteSelection();
        Text = Text.Insert(Pos, text);
        Pos += text.Length;
    }

    public void Backspace()
    {
        if (DeleteSelection())
            return;
        if (Pos > 0)
        {
            Text = Text.Remove(Pos - 1, 1);
            Pos--;
        }
    }

    public void DeleteChar()
    {
        if (DeleteSelection())
            return;
        if (Pos < Text.Length)
            Text = Text.Remove(Pos, 1);
    }

    /// <summary>Deletes the word before (dir&lt;0) / after the cursor.</summary>
    public void DeleteWord(int dir)
    {
        if (DeleteSelection())
            return;
        if (dir < 0)
        {
            int to = WordMotion.Backward(Text, Pos);
            Text = Text.Remove(to, Pos - to);
            Pos = to;
        }
        else
        {
            int to = WordMotion.Forward(Text, Pos);
            Text = Text.Remove(Pos, to - Pos);
        }
    }

    /// <summary>Moves with arrows (select moves with selection).</summary>
    public void Move(int delta, bool select) => MoveTo(Pos + delta, select);

    /// <summary>Moves to the start (select moves with selection).</summary>
    public void Home(bool select) => MoveTo(0, select);

    /// <summary>Moves to the end (select moves with selection).</summary>
    public void End(bool select) => MoveTo(Text.Length, select);

    /// <summary>Moves by words (select moves with selection).</summary>
    public void MoveWord(int dir, bool select) =>
        MoveTo(dir < 0 ? WordMotion.Backward(Text, Pos) : WordMotion.Forward(Text, Pos), select);

    private void MoveTo(int pos, bool select)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        if (select)
            Anchor ??= Pos; // Anchors at the start point
        else
            Anchor = null;
        Pos = pos;
        if (Anchor == Pos)
            Anchor = null; // Collapses to a caret
    }

    /// <summary>Clears the selection. Returns <see langword="true" /> if a selection existed; otherwise, <see langword="false" />.</summary>
    private bool DeleteSelection()
    {
        if (!HasSelection)
            return false;
        GetSelection(out int a, out int b);
        Text = Text.Remove(a, b - a);
        Pos = a;
        Anchor = null;
        return true;
    }
}
