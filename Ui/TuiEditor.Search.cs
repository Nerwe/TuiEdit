namespace TuiEdit;

/// <summary>TuiEditor: find, replace, grep and go-to.</summary>
internal sealed partial class TuiEditor
{
    /// <summary>Jumps to a row for CLI file:line (1-based, clamps past the end).</summary>
    internal void GoToLineNumber(int n)
    {
        _row = Math.Clamp(n - 1, 0, _buf.Count - 1);
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        UnfoldPath();
        TrackCol();
    }

    /// <summary>Jumps to the definition of the word under the caret (textual, no LSP).</summary>
    private void GoToDefinitionFlow()
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
            SetMessage(_loc["msg.goto.noword"]);
            return;
        }
        string name = line[start..end];
        List<DefHit> hits = DefinitionFinder.Find(StartDir(), _buf.FilePath, _buf.Lines, name, _row, _col);
        if (hits.Count == 0)
        {
            SetMessage(_loc.Format("msg.goto.none", name));
            return;
        }
        DefHit h = hits[0];
        PushJump();
        if (!string.Equals(h.File, _buf.FilePath, StringComparison.OrdinalIgnoreCase))
            OpenPicked(h.File);
        GoToPosition(h.Row + 1, h.Col + 1);
        _sel.Clear();
        if (hits.Count > 1)
            SetMessage(_loc.Format("msg.goto.found", hits.Count, name));
    }

    private void Find()
    {
        string? term = Prompt(_loc["prompt.find"], _lastSearch, "find", liveHighlight: true, showOptions: true);
        if (term is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (term.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        if (BadPattern(term)) return;
        _lastSearch = term;
        FindNext();
    }

    private bool BadPattern(string term)
    {
        if (_settings.SearchUseRegex &&
            TextBuffer.IsBadRegex(term, _settings.SearchMatchCase, _settings.SearchWholeWord))
        {
            SetMessage(_loc.Format("msg.search.badpattern", term));
            return true;
        }
        return false;
    }

    private void FindNext() => JumpSearch(wrap: true, backward: false);

    private void FindPrev() => JumpSearch(wrap: true, backward: true);

    /// <summary>Searches files: pattern plus folder, jumps on pick.</summary>
    private void GrepFlow()
    {
        string? pattern = Prompt(_loc["prompt.grep.pattern"], _lastSearch, "grep-pattern", liveHighlight: false, showOptions: true);
        if (pattern is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (pattern.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        string? dir = Prompt(_loc["prompt.grep.dir"], StartDir(), "grep-dir");
        if (dir is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (BadPattern(pattern)) return;
        _lastSearch = pattern;
        _grepHits.Clear();
        _grepHits.AddRange(Grep.Search(dir, pattern,
            _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex));
        if (_grepHits.Count == 0)
        {
            SetMessage(_loc.Format("msg.search.miss", pattern));
            return;
        }
        _dialog = new ModalDialog(ModalState.Grep(_loc, _grepHits), ApplyModalOutcome);
    }

    /// <summary>Jumps quickly to a project file (filters by name).</summary>
    private void QuickOpenFlow()
    {
        string root = StartDir();
        List<string> files = FileIndex.EnumerateFiles(root);
        if (files.Count == 0)
        {
            SetMessage(_loc.Format("msg.search.miss", "*"));
            return;
        }
        string? pickedPath = null;
        RunDialog(new CommandPaletteDialog(_settings, _store, ApplySettings, _ => { },
            loc => files.Select(f => (PaletteEntry)new FileEntry(f, RelativeToRoot(root, f))).ToList(),
            path => pickedPath = path));
        if (pickedPath is not null)
        {
            PushJump();
            OpenPicked(pickedPath);
        }
    }

    private static string RelativeToRoot(string root, string file)
    {
        try
        {
            return Path.GetRelativePath(root, file);
        }
        catch
        {
            return file;
        }
    }

    /// <summary>Runs the command line (F12): set/goto/find/save/quit.</summary>
    private void CommandLineFlow()
    {
        string? s = Prompt(_loc["cmdline.title"], string.Empty, "cmdline");
        if (s is null)
            return; // Esc stays quiet
        switch (CommandLine.Parse(s))
        {
            case null:
                SetMessage(_loc["cmdline.unknown"]);
                return;
            case CommandLineOp.Set set:
                // Buffer-scoped keys first, global settings after.
                string? err = set.Key switch
                {
                    "encoding" or "enc" =>
                        _buf.TrySetEncoding(set.Value) ? null : "cmdline.set.badvalue",
                    "ending" or "eol" =>
                        _buf.TrySetEnding(set.Value) ? null : "cmdline.set.badvalue",
                    "indent" =>
                        _buf.TrySetIndent(set.Value) ? null : "cmdline.set.badvalue",
                    _ => CommandLine.ApplySet(_settings, set.Key, set.Value),
                };
                if (err is not null)
                {
                    SetMessage(_loc[err]);
                    return;
                }
                _store.Save(_settings);
                ApplySettings();
                SetMessage(_loc.Format("cmdline.set.done", set.Key));
                return;
            case CommandLineOp.Goto g:
                PushJump();
                GoToPosition(g.Line, g.Col);
                _sel.Clear();
                return;
            case CommandLineOp.Find f:
                if (BadPattern(f.Term))
                    return;
                _lastSearch = f.Term;
                JumpSearch(wrap: true, backward: false);
                return;
            case CommandLineOp.Save:
                Save();
                return;
            case CommandLineOp.Earlier e:
                int back = e.Age is TimeSpan age ? _buf.UndoToAge(age) : _buf.UndoSteps(e.Steps);
                _sel.Clear();
                ClampCursor();
                SetMessage(_loc.Format("cmdline.earlier", back));
                return;
            case CommandLineOp.Later l:
                int fwd = l.Age is TimeSpan fage ? _buf.RedoToAge(fage) : _buf.RedoSteps(l.Steps);
                _sel.Clear();
                ClampCursor();
                SetMessage(_loc.Format("cmdline.later", fwd));
                return;
            case CommandLineOp.Quit:
                TryQuit();
                return;
        }
    }

    private void OpenGrepHit(int index)
    {
        if (index < 0 || index >= _grepHits.Count)
            return;
        GrepHit h = _grepHits[index];
        PushJump();
        _pendingGrepRow = h.Row + 1;
        OpenPicked(h.File);
        if (_pending == PendingOp.None)
        {
            GoToLineNumber(_pendingGrepRow);
            _pendingGrepRow = 0;
        }
    }

    /// <summary>Jumps to a match with a "k/N" counter.</summary>
    private void JumpSearch(bool wrap, bool backward)
    {
        if (string.IsNullOrEmpty(_lastSearch)) { Find(); return; }
        bool mc = _settings.SearchMatchCase, ww = _settings.SearchWholeWord, rx = _settings.SearchUseRegex;
        var hit = backward
            ? _buf.FindPrev(_lastSearch, _row, _col, mc, ww, wrap, rx)
            : _buf.FindNext(_lastSearch, _row, _col + 1, mc, ww, wrap, rx);
        if (hit is null) { SetMessage(_loc.Format("msg.search.miss", _lastSearch)); return; }
        (_row, _col) = (hit.Value.row, hit.Value.col);
        _sel.Clear(); // Jump clears the selection
        UnfoldPath();
        TrackCol();
        int total = _buf.CountMatches(_lastSearch, mc, ww, rx);
        int idx = _buf.MatchOrdinal(_lastSearch, _row, _col, mc, ww, rx);
        string key = hit.Value.wrapped ? "msg.search.wrap" : "msg.search.hit";
        SetMessage(_loc.Format(key, _lastSearch, idx, total));
    }

    internal static bool ShouldConfirmReplace(int count) => count > ReplaceConfirmThreshold;

    /// <summary>Replaces across the whole document instantly in one undo step.</summary>
    private void Replace()
    {
        string? term = Prompt(_loc["prompt.replace.find"], _lastSearch, "replace-find", liveHighlight: true, showOptions: true);
        if (term is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (term.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        string? rep = Prompt(_loc["prompt.replace.with"], _lastReplace, "replace-with");
        if (rep is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (BadPattern(term)) return;
        _lastSearch = term;
        _lastReplace = rep;
        int n = _buf.CountMatches(term,
            _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
        if (ShouldConfirmReplace(n))
        {
            _pendingReplaceTerm = term;
            _pendingReplaceRep = rep;
            var preview = _buf.PreviewReplace(term, rep,
                _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
            _dialog = new ModalDialog(ModalState.ConfirmReplace(_loc, term, n, preview), ApplyModalOutcome);
            return;
        }
        DoReplace(term, rep);
    }

    private void DoReplace(string term, string rep)
    {
        int n = _buf.ReplaceAll(term, rep, 0, 0,
            _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
        ClampCursor();
        SetMessage(n > 0
            ? _loc.Format("msg.replace.done", n)
            : _loc.Format("msg.search.miss", term));
    }

    /// <summary>Parses "N" / "N:M" / "$" (1-based; returns null for junk).</summary>
    internal static (int line, int col)? ParseGoTo(string s)
    {
        string t = s.Trim();
        if (t == "$")
            return (int.MaxValue, 0);
        int c = t.IndexOf(':');
        if (c < 0)
            return int.TryParse(t, out int n) && n > 0 ? (n, 0) : null;
        if (int.TryParse(t[..c], out int line) && line > 0
            && int.TryParse(t[(c + 1)..], out int col) && col > 0)
            return (line, col);
        return null;
    }

    private void GoToLine()
    {
        string? s = Prompt(_loc.Format("prompt.goto", _buf.Count), string.Empty, "goto");
        if (s is null) return;
        if (ParseGoTo(s) is (int line, int col))
        {
            PushJump();
            GoToPosition(line, col);
            _sel.Clear(); // Jump clears the selection
        }
        else SetMessage(_loc["msg.notnumber"]);
    }

    /// <summary>Toggles a search option (Alt+C/W/R in the prompt). Returns <see langword="true"/> if the key was consumed; otherwise, <see langword="false"/>.</summary>
    internal bool ToggleSearchOption(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.C: _settings.SearchMatchCase = !_settings.SearchMatchCase; break;
            case ConsoleKey.W: _settings.SearchWholeWord = !_settings.SearchWholeWord; break;
            case ConsoleKey.R: _settings.SearchUseRegex = !_settings.SearchUseRegex; break;
            default: return false;
        }
        _store.Save(_settings);
        return true;
    }

    internal string EffectiveSearchTerm => _liveSearch ?? _lastSearch;
}
