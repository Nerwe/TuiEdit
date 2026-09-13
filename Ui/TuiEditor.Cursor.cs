namespace TuiEdit;

/// <summary>TuiEditor: cursor, selection and text editing ops.</summary>
internal sealed partial class TuiEditor
{
    private string CurLine => _buf.GetLine(_row);

    /// <summary>
    /// Remembers the visual cursor column for Up/Down
    /// (character offsets fail - tabs vary in width).
    /// </summary>
    private void TrackCol() => _desiredCol = TabStops.VisualWidth(_buf.GetLine(_row), _col);

    /// <summary>Deletes the selection (if any), moving the cursor to its start.</summary>
    private bool DeleteSelection()
    {
        if (!_sel.HasSelection(_row, _col))
            return false;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        (_row, _col) = _buf.DeleteRange(sr, sc, er, ec);
        if (er > sr)
        {
            DocTab t = _docs[_active];
            t.DropMarks(sr + 1, er);
            t.ShiftMarks(sr + 1, sr - er);
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

    /// <summary>Gets the lines affected by the selection (a row with ec==0 is excluded).</summary>
    private (int First, int Last) SelectionLineRange()
    {
        if (!_sel.HasSelection(_row, _col))
            return (_row, _row); // no selection — stale anchor must not widen to line 0
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
        t.ClampMarks(_buf.Count);
        bool set = t.ToggleBookmark(_row);
        SetMessage(set ? _loc["msg.bookmark.set"] : _loc["msg.bookmark.cleared"]);
    }

    internal void NextBookmark()
    {
        DocTab t = _docs[_active];
        t.ClampMarks(_buf.Count);
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
        t.ClampMarks(_buf.Count);
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
    /// Handles input with auto-pairs: types over its own closer by stepping right,
    /// inserts a pair for an opener. Returns <see langword="true"/> if handled; otherwise, <see langword="false"/>.
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
                _docs[_active].ShiftMarks(wr, -1);
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
                _docs[_active].ShiftMarks(dr + 1, -1);
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
        _docs[_active].ShiftMarks(e + 1, e - s + 1);
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

    /// <summary>Runs the line block through a shell command (:pipe).</summary>
    private void ShellFilterFlow()
    {
        string? cmd = Prompt(_loc["prompt.shellfilter"], "", "shell");
        if (string.IsNullOrWhiteSpace(cmd))
            return;
        ShellFilterWith(cmd);
    }

    /// <summary>
    /// Runs the selection rows (or the current row) through cmd, replacing them
    /// with the output in a single undo entry (empty output deletes). Errors keep
    /// the text and show a message. Testable: no prompt here.
    /// </summary>
    internal void ShellFilterWith(string command)
    {
        var (s, e) = LineBlock();
        string input = string.Join("\n", _buf.Lines.GetRange(s, e - s + 1));
        var (exe, args) = ShellFilter.Split(command);
        List<string> output;
        try
        {
            output = ShellFilter.Run(exe, args, input);
        }
        catch (Exception ex)
        {
            SetMessage(DisplayError(ex));
            return;
        }
        _buf.ReplaceLines(s, e, output);
        _docs[_active].ShiftMarks(e + 1, output.Count - (e - s + 1));
        _sel.Clear();
        if (output.Count == 0)
        {
            _row = Math.Min(s, _buf.Count - 1);
            _col = 0;
        }
        else
        {
            _row = s + output.Count - 1;
            _col = output[^1].Length;
        }
        ClampCursor();
        TrackCol();
    }

    private void MoveLineBlock(int dir)
    {
        var (s, e) = LineBlock();
        bool ok = dir < 0 ? _buf.MoveLinesUp(s, e) : _buf.MoveLinesDown(s, e);
        if (!ok) return;
        _docs[_active].MoveMarks(s, e, dir);
        _row += dir;
        _sel.Clear();
        ClampCursor();
        TrackCol();
    }

    private void CutLine()
    {
        ClampCursor();
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            StoreYank(_buf.GetRangeText(sr, sc, er, ec).ToList(), isDelete: true);
            int n = SelectionLength(sr, sc, er, ec);
            DeleteSelection();
            _clipboardSvc.Export(_clipboard);
            SetMessage(_loc.Format("msg.cut.sel", n));
            return;
        }
        int crow = _row, cbefore = _buf.Count;
        StoreYank([_buf.CutLine(_row)], isDelete: true);
        if (_buf.Count < cbefore)
        {
            _docs[_active].DropMarks(crow, crow);
            _docs[_active].ShiftMarks(crow + 1, -1);
        }
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        TrackCol();
        SetMessage(_loc["msg.cut.line"]);
    }

    private void CopyLine()
    {
        ClampCursor();
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            StoreYank(_buf.GetRangeText(sr, sc, er, ec).ToList(), isDelete: false);
            _clipboardSvc.Export(_clipboard);
            SetMessage(_loc.Format("msg.copy.sel", SelectionLength(sr, sc, er, ec)));
            return;
        }
        StoreYank([_buf.GetLine(_row)], isDelete: false);
        _clipboardSvc.Export(_clipboard);
        SetMessage(_loc["msg.copy.line"]);
    }

    private void Paste()
    {
        List<string>? src = TakePasteSource();
        if (src is null) { SetMessage(_loc["msg.register.empty"]); return; }
        if (src.Count == 0) { SetMessage(_loc["msg.paste.empty"]); return; }
        ClampCursor();
        DeleteSelection();
        int pr = _row;
        _buf.PasteLines(_row, _col, src);
        if (src.Count == 1) _col += src[0].Length;
        else
        {
            _docs[_active].ShiftMarks(pr + 1, src.Count - 1);
            _row += src.Count - 1; _col = src[^1].Length;
        }
        TrackCol();
    }

    /// <summary>Key-driven paste with held-key coalescing (see <see cref="DrainPasteRun"/>).</summary>
    private void CoalescedKeyPaste()
    {
        List<string>? src = TakePasteSource();
        if (src is null) { SetMessage(_loc["msg.register.empty"]); return; }
        if (src.Count == 0) { Paste(); return; } // empty path unchanged (message, no drain)
        string repeat = string.Join("\n", src);
        InsertPastedText(repeat + DrainPasteRun(repeat));
    }

    /// <summary>
    /// Inserts text as one buffer mutation: joining the clipboard with "\n" round-trips
    /// buffer lines exactly (lines never contain "\n"), so this matches PasteLines
    /// content- and cursor-wise while costing a single undo entry and a single frame.
    /// </summary>
    private void InsertPastedText(string text)
    {
        _speedCount = 0; // paste is not typing
        DeleteSelection();
        ClampCursor();
        int pr = _row;
        int pbefore = _buf.Count;
        (_row, _col) = _buf.InsertText(_row, _col, text);
        if (_buf.Count > pbefore)
        {
            _docs[_active].ShiftMarks(pr + 1, _buf.Count - pbefore);
        }
        ClampCursor();
        TrackCol();
    }

    /// <summary>
    /// Merges a held-paste burst into one insertion: pulls paste inputs already pending
    /// behind the current one (key auto-repeat, terminal paste bursts) and concatenates
    /// them, so N queued pastes cost one mutation and one frame instead of a backlog
    /// that keeps inserting after key release. Merges non-empty editor pastes only;
    /// the first anything-else lands in <see cref="_heldEvent"/> for the next loop
    /// iteration (never swallowed). Editor text only: callers ensure no dialog, menu,
    /// or focused panel is open.
    /// </summary>
    private string DrainPasteRun(string repeat)
    {
        var sb = new System.Text.StringBuilder();
        while (InputReader.TryReadPending() is InputEvent ev)
        {
            if (ev is PasteInput p && p.Text.Length > 0) { sb.Append(p.Text); continue; }
            if (ev is KeyInput ki && _keys.Map(ki.Key) == EditorCommand.Paste)
            {
                sb.Append(repeat);
                continue;
            }
            _heldEvent = ev;
            break;
        }
        return sb.ToString();
    }

    /// <summary>Picks a one-shot register for the next yank/paste (Ctrl+X).</summary>
    private void RegisterPickFlow()
    {
        string? s = Prompt(_loc["prompt.register"], "", "register");
        if (string.IsNullOrEmpty(s))
            return;
        if (!PickRegister(s[0]))
        {
            SetMessage(_loc["msg.register.bad"]);
            return;
        }
        SetMessage(_loc.Format("msg.register.set", char.ToLowerInvariant(s[0])));
    }

    /// <summary>Takes the selection (or the word under a collapsed cursor) as a surround range.</summary>
    private bool SurroundRange(out int sr, out int sc, out int er, out int ec)
    {
        if (!_sel.HasSelection(_row, _col))
            SelectWordAt(_row, _col);
        if (!_sel.HasSelection(_row, _col))
        {
            (sr, sc, er, ec) = (0, 0, 0, 0);
            return false;
        }
        (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        return true;
    }

    /// <summary>Wraps the range in a prompted pair (single undo, word fallback when collapsed).</summary>
    private void SurroundAddFlow()
    {
        if (!SurroundRange(out int sr, out int sc, out int er, out int ec))
        {
            SetMessage(_loc["msg.surround.empty"]);
            return;
        }
        string? s = Prompt(_loc["prompt.surround"], "", "surround");
        if (string.IsNullOrEmpty(s))
            return;
        char open = s[0];
        char close = TextBuffer.CloserForSurround(open);
        _buf.WrapRange(sr, sc, er, ec, open, close);
        _sel.Clear();
        _row = er;
        _col = sr == er ? ec + 2 : ec + 1;
        ClampCursor();
        TrackCol();
        SetMessage(_loc["msg.surround.done"]);
    }

    /// <summary>Swaps the detected pair around the range for a prompted one (single undo).</summary>
    private void SurroundChangeFlow()
    {
        if (!SurroundRange(out int sr, out int sc, out int er, out int ec))
        {
            SetMessage(_loc["msg.surround.empty"]);
            return;
        }
        if (_buf.PeekSurround(sr, sc, er, ec) is not (char oldOpen, char oldClose))
        {
            SetMessage(_loc["msg.surround.missing"]);
            return;
        }
        string? s = Prompt(_loc["prompt.surround"], "", "surround");
        if (string.IsNullOrEmpty(s))
            return;
        char open = s[0];
        char close = TextBuffer.CloserForSurround(open);
        if (!_buf.ChangeSurround(sr, sc, er, ec, oldOpen, oldClose, open, close))
        {
            SetMessage(_loc["msg.surround.missing"]);
            return;
        }
        _sel.Clear();
        ClampCursor();
        TrackCol();
        SetMessage(_loc["msg.surround.done"]);
    }

    /// <summary>Deletes the detected pair around the range (single undo).</summary>
    private void SurroundDeleteFlow()
    {
        if (!SurroundRange(out int sr, out int sc, out int er, out int ec))
        {
            SetMessage(_loc["msg.surround.empty"]);
            return;
        }
        if (_buf.PeekSurround(sr, sc, er, ec) is not (char open, char close))
        {
            SetMessage(_loc["msg.surround.missing"]);
            return;
        }
        if (!_buf.UnwrapRange(sr, sc, er, ec, open, close))
        {
            SetMessage(_loc["msg.surround.missing"]);
            return;
        }
        _sel.Clear();
        _row = er;
        _col = sr == er ? Math.Max(0, ec - 1) : ec;
        ClampCursor();
        TrackCol();
        SetMessage(_loc["msg.surround.done"]);
    }
}
