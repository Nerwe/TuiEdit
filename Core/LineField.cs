namespace TuiEdit;

/// <summary>
/// Однострочное поле ввода: текст, курсор, якорь выделения.
/// Чистая модель — общее для поля имени в менеджере и промптов.
/// </summary>
public sealed class LineField
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

    /// <summary>Ввод поверх выделения.</summary>
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

    /// <summary>Удаление слова до (dir&lt;0) / после курсора.</summary>
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

    /// <summary>Стрелки (select — с выделением).</summary>
    public void Move(int delta, bool select) => MoveTo(Pos + delta, select);

    /// <summary>В начало/конец (select — с выделением).</summary>
    public void Home(bool select) => MoveTo(0, select);

    /// <summary>В начало/конец (select — с выделением).</summary>
    public void End(bool select) => MoveTo(Text.Length, select);

    /// <summary>По словам (select — с выделением).</summary>
    public void MoveWord(int dir, bool select) =>
        MoveTo(dir < 0 ? WordMotion.Backward(Text, Pos) : WordMotion.Forward(Text, Pos), select);

    private void MoveTo(int pos, bool select)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        if (select)
            Anchor ??= Pos; // якорь в точке старта
        else
            Anchor = null;
        Pos = pos;
        if (Anchor == Pos)
            Anchor = null; // схлопнулось
    }

    /// <summary>Стереть выделение (true если было).</summary>
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
