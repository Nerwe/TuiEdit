namespace TuiEdit;

/// <summary>TuiEditor: multi-cursor editing (extra carets fan out edits and motion).</summary>
internal sealed partial class TuiEditor
{
    private const int MaxCarets = 64;

    private MultiCaret XC => _docs[_active].Carets;

    private bool HasExtraCarets => XC.HasMultiple;

    private void ClearExtraCarets() => XC.Clear();

    /// <summary>Clamps extra carets to the document and drops ones merged into the primary.</summary>
    private void SyncExtraCarets()
    {
        XC.Clamp(_buf.Count, r => _buf.GetLine(r).Length);
        // Extras that landed on the primary are redundant.
        var keep = XC.Ordered.Where(c => c != (_row, _col)).ToList();
        XC.Clear();
        foreach ((int r, int c) in keep)
            XC.Add(r, c, _row, _col);
    }

    private void CaretAddAbove() => CaretAddVertical(-1);

    private void CaretAddBelow() => CaretAddVertical(1);

    /// <summary>Adds a caret one visible row above/below the outermost caret, same column when possible.</summary>
    private void CaretAddVertical(int dir)
    {
        ClampCursor();
        // Grow from the edge in this direction (not from the primary),
        // otherwise repeated presses land on the same row and dedup.
        int r = _row;
        int c = _col;
        foreach ((int er, int ec) in XC.Ordered)
        {
            if ((dir < 0 && er < r) || (dir > 0 && er > r))
            {
                r = er;
                c = ec;
            }
        }
        if ((dir < 0 && r > 0) || (dir > 0 && r < _buf.Count - 1))
        {
            do { r += dir; } while (r > 0 && r < _buf.Count - 1 && FoldHidden(r));
        }
        c = Math.Min(c, _buf.GetLine(r).Length);
        if (XC.Add(r, c, _row, _col))
            SetMessage(_loc.Format("msg.carets", XC.Count + 1));
    }

    /// <summary>Adds a caret at the next occurrence of the word under the primary caret.</summary>
    private void CaretAddNext()
    {
        ClampCursor();
        string line = CurLine;
        int start = _col;
        while (start > 0 && TextBuffer.IsWordChar(line[start - 1]))
            start--;
        int end = _col;
        while (end < line.Length && TextBuffer.IsWordChar(line[end]))
            end++;
        if (end <= start)
        {
            SetMessage(_loc["msg.carets.noword"]);
            return;
        }
        string term = line[start..end];
        if (XC.Count + 1 >= MaxCarets)
        {
            SetMessage(_loc.Format("msg.carets", XC.Count + 1));
            return;
        }
        // Search forward from the end of the current word, then wrap once.
        for (int pass = 0; pass < 2; pass++)
        {
            int fromRow = pass == 0 ? _row : 0;
            for (int r = fromRow; r < _buf.Count; r++)
            {
                string l = _buf.GetLine(r);
                int from = r == _row && pass == 0 ? end : 0;
                int to = r == _row && pass == 1 ? start : l.Length;
                int idx = l.IndexOf(term, from, Math.Max(0, to - from), StringComparison.Ordinal);
                if (idx < 0)
                    continue;
                if ((r == _row && idx == start) || !XC.Add(r, idx, _row, _col))
                    continue;
                SetMessage(_loc.Format("msg.carets", XC.Count + 1));
                return;
            }
        }
        SetMessage(_loc["msg.carets.none"]);
    }

    /// <summary>Moves the primary and every extra caret by the same motion.</summary>
    private void MoveAllCarets(Func<int, int, (int Row, int Col)> step)
    {
        (int Row, int Col) primary = step(_row, _col);
        var moved = new List<(int, int)>();
        foreach ((int r, int c) in XC.Ordered)
        {
            (int nr, int nc) = step(r, c);
            if ((nr, nc) != primary)
                moved.Add((nr, nc));
        }
        _row = primary.Row;
        _col = primary.Col;
        TrackCol();
        XC.Clear();
        foreach ((int r, int c) in moved)
            XC.Add(r, c, _row, _col);
    }

    private (int Row, int Col) StepLeft(int r, int c)
    {
        r = Math.Clamp(r, 0, _buf.Count - 1);
        c = Math.Clamp(c, 0, _buf.GetLine(r).Length);
        if (c > 0)
            return (r, c - 1);
        if (r > 0)
            return (r - 1, _buf.GetLine(r - 1).Length);
        return (r, c);
    }

    private (int Row, int Col) StepRight(int r, int c)
    {
        r = Math.Clamp(r, 0, _buf.Count - 1);
        c = Math.Clamp(c, 0, _buf.GetLine(r).Length);
        if (c < _buf.GetLine(r).Length)
            return (r, c + 1);
        if (r < _buf.Count - 1)
            return (r + 1, 0);
        return (r, c);
    }

    private (int Row, int Col) StepUp(int r, int c)
    {
        if (r > 0)
        {
            do { r--; } while (r > 0 && FoldHidden(r));
            c = TabStops.CharIndexAtVisual(_buf.GetLine(r), TabStops.VisualWidth(_buf.GetLine(r), c));
        }
        return (r, c);
    }

    private (int Row, int Col) StepDown(int r, int c)
    {
        if (r < _buf.Count - 1)
        {
            do { r++; } while (r < _buf.Count - 1 && FoldHidden(r));
            c = TabStops.CharIndexAtVisual(_buf.GetLine(r), TabStops.VisualWidth(_buf.GetLine(r), c));
        }
        return (r, c);
    }

    /// <summary>
    /// Applies an edit at the primary and every extra caret, bottom-up so earlier
    /// edits never shift later positions. Merges everything into one undo step.
    /// </summary>
    private void FanOutEdits(Func<int, int, (int Row, int Col)> edit)
    {
        var all = new List<(int Row, int Col)> { (_row, _col) };
        all.AddRange(XC.Ordered);
        var order = all
            .Select((p, i) => (Pos: p, Index: i))
            .OrderByDescending(x => x.Pos.Row)
            .ThenByDescending(x => x.Pos.Col)
            .ToList();
        var result = new (int Row, int Col)[all.Count];
        int before = _buf.UndoDepth;
        foreach (var item in order)
            result[item.Index] = edit(item.Pos.Row, item.Pos.Col);
        _buf.CoalesceUndo(_buf.UndoDepth - before);
        _row = result[0].Row;
        _col = result[0].Col;
        XC.Clear();
        for (int i = 1; i < result.Length; i++)
            XC.Add(result[i].Row, result[i].Col, _row, _col);
        SyncExtraCarets();
        ClampCursor();
        TrackCol();
    }

    private void MultiInsertChar(char c)
    {
        FanOutEdits((r, col) =>
        {
            _buf.InsertChar(r, col, c);
            return (r, col + 1);
        });
    }

    private void MultiBackspace()
    {
        FanOutEdits((r, col) =>
        {
            (int nr, int nc) = _buf.Backspace(r, col);
            if (col == 0 && r > 0 && (nr != r || nc != col))
                _docs[_active].ShiftMarks(r, -1);
            return (nr, nc);
        });
    }

    private void MultiDelete()
    {
        FanOutEdits((r, col) =>
        {
            int len = _buf.GetLine(r).Length;
            int n = _buf.Count;
            (int nr, int nc) = _buf.Delete(r, col);
            if (col >= len && r + 1 < n)
                _docs[_active].ShiftMarks(r + 1, -1);
            return (nr, nc);
        });
    }

    private void MultiEnter()
    {
        FanOutEdits((r, col) =>
        {
            (int nr, int nc) = _buf.SplitLine(r, col);
            _docs[_active].ShiftMarks(nr, 1);
            return (nr, nc);
        });
    }

    private void MultiTab()
    {
        string indent = _buf.IndentString;
        FanOutEdits((r, col) =>
        {
            _buf.InsertString(r, col, indent);
            return (r, col + indent.Length);
        });
    }
}
