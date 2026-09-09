namespace TuiEdit;

/// <summary>TuiEditor: cursor, selection and text editing ops.</summary>
internal sealed partial class TuiEditor
{
    private string CurLine => _buf.GetLine(_row);

    /// <summary>
    /// Запоминает визуальную колонку курсора для Up/Down
    /// (в символах нельзя — табы разной ширины).
    /// </summary>
    private void TrackCol() => _desiredCol = TabStops.VisualWidth(_buf.GetLine(_row), _col);

    /// <summary>Удаляет выделение (если есть), курсор — в его начало.</summary>
    private bool DeleteSelection()
    {
        if (!_sel.HasSelection(_row, _col))
            return false;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        (_row, _col) = _buf.DeleteRange(sr, sc, er, ec);
        if (er > sr)
        {
            DocTab t = _docs[_active];
            t.DropBookmarks(sr + 1, er);
            t.ShiftBookmarks(sr + 1, sr - er);
            t.DropFolds(sr + 1, er);
            t.ShiftFolds(sr + 1, sr - er);
        }
        _sel.Clear();
        TrackCol();
        return true;
    }

    private int SelectionLength(int sr, int sc, int er, int ec)
    {
        int n = 0;
        for (int r = sr; r <= er; r++)
            n += (r, sc, ec) switch
            {
                var (row, s, e) when row == sr && row == er => e - s,
                var (row, s, _) when row == sr => _buf.GetLine(row).Length - s,
                var (row, _, e) when row == er => e,
                var (row, _, _) => _buf.GetLine(row).Length,
            };
        return n;
    }

    /// <summary>Строки, затронутые выделением (строка с ec==0 не включается).</summary>
    private (int First, int Last) SelectionLineRange()
    {
        var (sr, _, er, ec) = _sel.Normalize(_row, _col);
        if (ec == 0 && er > sr)
            er--;
        return (sr, er);
    }

    private void IndentSelection()
    {
        var (sr, er) = SelectionLineRange();
        int[] added = _buf.IndentLines(sr, er, _buf.IndentString);
        if (_sel.AnchorRow >= sr && _sel.AnchorRow <= er)
            _sel.AnchorCol += added[_sel.AnchorRow - sr];
        if (_row >= sr && _row <= er)
            _col += added[_row - sr];
        TrackCol();
    }

    private void ToggleComment()
    {
        string lc = CurrentGrammar()?.LineComment ?? string.Empty;
        if (string.IsNullOrEmpty(lc))
        {
            SetMessage(_loc["msg.nocomment"]);
            return;
        }
        var (sr, er) = SelectionLineRange();
        int[] delta = _buf.ToggleLineComment(sr, er, lc);
        if (_sel.AnchorRow >= sr && _sel.AnchorRow <= er)
            _sel.AnchorCol = Math.Max(0, _sel.AnchorCol + delta[_sel.AnchorRow - sr]);
        if (_row >= sr && _row <= er)
            _col = Math.Max(0, _col + delta[_row - sr]);
        TrackCol();
    }

    private void JumpToBracket()
    {
        var pair = BracketPair();
        if (pair is null)
            return;
        _sel.Clear();
        _row = pair.Value.PairRow;
        _col = pair.Value.PairCol;
        UnfoldPath();
        TrackCol();
    }

    internal void ToggleBookmark()
    {
        DocTab t = _docs[_active];
        t.ClampBookmarks(_buf.Count);
        bool set = t.ToggleBookmark(_row);
        SetMessage(set ? _loc["msg.bookmark.set"] : _loc["msg.bookmark.cleared"]);
    }

    internal void NextBookmark()
    {
        DocTab t = _docs[_active];
        t.ClampBookmarks(_buf.Count);
        if (t.Bookmarks.Count == 0)
        {
            SetMessage(_loc["msg.bookmark.none"]);
            return;
        }
        var after = t.Bookmarks.GetViewBetween(_row + 1, int.MaxValue);
        _row = after.Count > 0 ? after.Min : t.Bookmarks.Min;
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        _sel.Clear();
        UnfoldPath();
        TrackCol();
    }

    private bool FoldHidden(int row) => Folding.IsHidden(_buf.Lines, _docs[_active].Folds, row);

    private int FoldStart(int row) => FoldStartAt(_buf.Lines, _docs[_active].Folds, row);

    private void UnfoldPath() =>
        Folding.UnfoldContaining(_buf.Lines, _docs[_active].Folds, _row);

    internal void ToggleFold()
    {
        DocTab t = _docs[_active];
        t.ClampFolds(_buf.Count);
        if (t.Folds.Remove(_row))
        {
            SetMessage(_loc["msg.fold.opened"]);
            return;
        }
        int? outer = null;
        foreach (int f in t.Folds)
        {
            if (f >= _row)
                break;
            if (Folding.EndOf(_buf.Lines, f) >= _row)
                outer = f;
        }
        if (outer is not null)
        {
            t.Folds.Remove(outer.Value);
            SetMessage(_loc["msg.fold.opened"]);
            return;
        }
        if (!Folding.CanFold(_buf.Lines, _row))
        {
            SetMessage(_loc["msg.fold.none"]);
            return;
        }
        t.Folds.Add(_row);
        SetMessage(_loc["msg.fold.closed"]);
    }

    private void CompleteWord()
    {
        string line = CurLine;
        int start = _col;
        while (start > 0 && TextBuffer.IsWordChar(line[start - 1]))
            start--;
        string prefix = line[start.._col];
        if (prefix.Length < 2)
        {
            SetMessage(_loc["msg.complete.none"]);
            return;
        }
        List<string> words = Completion.Collect(_buf, prefix);
        if (words.Count == 0)
        {
            SetMessage(_loc["msg.complete.none"]);
            return;
        }
        if (words.Count == 1)
        {
            ApplyCompletion(prefix, words[0]);
            return;
        }
        _pendingCompletePrefix = prefix;
        _dialog = new ModalDialog(ModalState.Complete(_loc, words), ApplyModalOutcome);
    }

    private void ApplyCompletion(string prefix, string word)
    {
        string rest = word[prefix.Length..];
        _buf.InsertString(_row, _col, rest);
        _col += rest.Length;
        TrackCol();
    }

    private (int Row, int Col, int PairRow, int PairCol)? BracketPair()
    {
        if (_row < 0 || _row >= _buf.Count)
            return null;
        string line = _buf.GetLine(_row);
        int at = -1;
        if (_col >= 0 && _col < line.Length && BracketMatcher.IsBracket(line[_col]))
            at = _col;
        else if (_col > 0 && _col <= line.Length && BracketMatcher.IsBracket(line[_col - 1]))
            at = _col - 1;
        if (at < 0)
            return null;
        var pair = BracketMatcher.FindMatch(_buf, _docs[_active].Highlighter, CurrentGrammar(), _row, at);
        return pair is null ? null : (_row, at, pair.Value.Row, pair.Value.Col);
    }

    private void UnindentSelectionOrLine()
    {
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, er) = SelectionLineRange();
            int[] removed = _buf.UnindentLines(sr, er, _buf.IndentString);
            if (_sel.AnchorRow >= sr && _sel.AnchorRow <= er)
                _sel.AnchorCol = Math.Max(0, _sel.AnchorCol - removed[_sel.AnchorRow - sr]);
            if (_row >= sr && _row <= er)
                _col = Math.Max(0, _col - removed[_row - sr]);
        }
        else
        {
            int[] removed = _buf.UnindentLines(_row, _row, _buf.IndentString);
            _col = Math.Max(0, _col - removed[0]);
        }
        TrackCol();
    }

    private void SelectAll()
    {
        _sel.Start(0, 0);
        _row = _buf.Count - 1;
        _col = _buf.GetLine(_row).Length;
        TrackCol();
        SetMessage(_loc.Format("status.sel", SelectionLength(0, 0, _row, _col)));
    }

    /// <summary>
    /// Ввод с автопарами: закрывающий поверх своего — шаг вправо,
    /// открывающий — вставить пару. True — обработано.
    /// </summary>
    private bool TryAutoPair(char c)
    {
        string line = _buf.GetLine(_row);
        if (AutoPair.ShouldSkip(line, _col, c))
        {
            _col++;
            TrackCol();
            return true;
        }
        char closer = AutoPair.CloserFor(c);
        if (closer != '\0' && AutoPair.ShouldPair(line, _col, c))
        {
            (_row, _col) = _buf.InsertPair(_row, _col, c, closer);
            TrackCol();
            return true;
        }
        return false;
    }

    private void ClampCursor()
    {
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Clamp(_col, 0, _buf.GetLine(_row).Length);
    }

    private void MoveLeft()
    {
        ClampCursor();
        switch (_col, _row)
        {
            case ( > 0, _): _col--; break;
            case (0, > 0): _row--; _col = _buf.GetLine(_row).Length; break;
            default: break;
        }
        TrackCol();
    }

    private void MoveRight()
    {
        ClampCursor();
        switch (_col < CurLine.Length, _row < _buf.Count - 1)
        {
            case (true, _): _col++; break;
            case (false, true): _row++; _col = 0; break;
            default: break;
        }
        TrackCol();
    }

    private void MoveUp()
    {
        if (_row > 0)
        {
            do { _row--; } while (_row > 0 && FoldHidden(_row));
            _col = TabStops.CharIndexAtVisual(_buf.GetLine(_row), _desiredCol);
        }
    }

    private void MoveDown()
    {
        if (_row < _buf.Count - 1)
        {
            do { _row++; } while (_row < _buf.Count - 1 && FoldHidden(_row));
            _col = TabStops.CharIndexAtVisual(_buf.GetLine(_row), _desiredCol);
        }
    }

    private void GoHome() { ClampCursor(); _col = 0; _desiredCol = 0; }

    private void GoEnd() { ClampCursor(); _col = CurLine.Length; TrackCol(); }

    private void GoDocStart() { _row = 0; _col = 0; _desiredCol = 0; }

    private void GoDocEnd() { _row = _buf.Count - 1; _col = _buf.GetLine(_row).Length; TrackCol(); }

    private void MovePage(int dir)
    {
        int h = TextHeight();
        _row = Math.Clamp(_row + dir * Math.Max(1, h - 1), 0, _buf.Count - 1);
        while (FoldHidden(_row) && _row > 0 && _row < _buf.Count - 1)
            _row += dir;
        _col = TabStops.CharIndexAtVisual(_buf.GetLine(_row), _desiredCol);
    }

    private void MoveWordLeft()
    {
        ClampCursor();
        if (_col == 0)
        {
            if (_row == 0)
                return;
            _row--;
            _col = _buf.GetLine(_row).Length;
        }
        _col = WordMotion.Backward(CurLine, _col);
        TrackCol();
    }

    private void MoveWordRight()
    {
        ClampCursor();
        if (_col >= CurLine.Length)
        {
            if (_row >= _buf.Count - 1)
                return;
            _row++;
            _col = 0;
        }
        _col = WordMotion.Forward(CurLine, _col);
        TrackCol();
    }

    private void DeleteWordBefore()
    {
        ClampCursor();
        if (DeleteSelection()) return;
        if (_col == 0 && _row == 0) return;
        if (_col == 0)
        {
            int wr = _row;
            (_row, _col) = _buf.Backspace(_row, _col);
            if (wr > 0)
            {
                _docs[_active].ShiftBookmarks(wr, -1);
                _docs[_active].ShiftFolds(wr, -1);
            }
            TrackCol();
            return;
        }
        int target = WordMotion.Backward(CurLine, _col);
        (_row, _col) = _buf.DeleteRange(_row, target, _row, _col);
        TrackCol();
    }

    private void DeleteWordAfter()
    {
        ClampCursor();
        if (DeleteSelection()) return;
        string line = CurLine;
        if (_col >= line.Length)
        {
            int dr = _row;
            int dn = _buf.Count;
            (_row, _col) = _buf.Delete(_row, _col);
            if (dr + 1 < dn)
            {
                _docs[_active].ShiftBookmarks(dr + 1, -1);
                _docs[_active].ShiftFolds(dr + 1, -1);
            }
            TrackCol();
            return;
        }
        int end = WordMotion.Forward(line, _col);
        (_row, _col) = _buf.DeleteRange(_row, _col, _row, end);
        TrackCol();
    }

    private (int start, int end) LineBlock()
    {
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, _, er, _) = _sel.Normalize(_row, _col);
            return (sr, er);
        }
        return (_row, _row);
    }

    private void DuplicateBlock()
    {
        var (s, e) = LineBlock();
        int copy = _buf.DuplicateLines(s, e);
        _docs[_active].ShiftBookmarks(e + 1, e - s + 1);
        _docs[_active].ShiftFolds(e + 1, e - s + 1);
        _row = copy + (_row - s);
        _sel.Clear();
        ClampCursor();
        TrackCol();
    }

    private void SortBlock()
    {
        var (s, e) = LineBlock();
        _buf.SortLines(s, e);
        _sel.Clear();
        ClampCursor();
        TrackCol();
        SetMessage(_loc.Format("msg.sorted", e - s + 1));
    }

    private void MoveLineBlock(int dir)
    {
        var (s, e) = LineBlock();
        bool ok = dir < 0 ? _buf.MoveLinesUp(s, e) : _buf.MoveLinesDown(s, e);
        if (!ok) return;
        _docs[_active].MoveBookmarks(s, e, dir);
        _docs[_active].MoveFolds(s, e, dir);
        _row += dir;
        _sel.Clear();
        ClampCursor();
        TrackCol();
    }

    private void CutLine()
    {
        ClampCursor();
        _clipboard.Clear();
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            _clipboard.AddRange(_buf.GetRangeText(sr, sc, er, ec));
            int n = SelectionLength(sr, sc, er, ec);
            DeleteSelection();
            SystemClipboard.TryExport(_clipboard);
            SetMessage(_loc.Format("msg.cut.sel", n));
            return;
        }
        int crow = _row, cbefore = _buf.Count;
        _clipboard.Add(_buf.CutLine(_row));
        if (_buf.Count < cbefore)
        {
            _docs[_active].DropBookmarks(crow, crow);
            _docs[_active].ShiftBookmarks(crow + 1, -1);
            _docs[_active].DropFolds(crow, crow);
            _docs[_active].ShiftFolds(crow + 1, -1);
        }
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        TrackCol();
        SetMessage(_loc["msg.cut.line"]);
    }

    private void CopyLine()
    {
        ClampCursor();
        _clipboard.Clear();
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            _clipboard.AddRange(_buf.GetRangeText(sr, sc, er, ec));
            SystemClipboard.TryExport(_clipboard);
            SetMessage(_loc.Format("msg.copy.sel", SelectionLength(sr, sc, er, ec)));
            return;
        }
        _clipboard.Add(_buf.GetLine(_row));
        SystemClipboard.TryExport(_clipboard);
        SetMessage(_loc["msg.copy.line"]);
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) { SetMessage(_loc["msg.paste.empty"]); return; }
        ClampCursor();
        DeleteSelection();
        int pr = _row;
        _buf.PasteLines(_row, _col, _clipboard);
        if (_clipboard.Count == 1) _col += _clipboard[0].Length;
        else
        {
            _docs[_active].ShiftBookmarks(pr + 1, _clipboard.Count - 1);
            _docs[_active].ShiftFolds(pr + 1, _clipboard.Count - 1);
            _row += _clipboard.Count - 1; _col = _clipboard[^1].Length;
        }
        TrackCol();
    }
}
