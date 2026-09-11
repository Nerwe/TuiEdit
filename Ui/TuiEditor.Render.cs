using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>TuiEditor: frame rendering and text drawing.</summary>
internal sealed partial class TuiEditor
{
    private static int TextHeight()
    {
        int h;
        try { h = Console.WindowHeight; } catch { return 10; }
        return Math.Max(1, h - 2);
    }

    private int VisibleRow(int row)
    {
        int v = 0;
        for (int r = 0; r < row && r < _buf.Count; r++)
            if (!FoldHidden(r))
                v++;
        return v;
    }

    private int FileRowAt(int vis)
    {
        int v = -1;
        for (int r = 0; r < _buf.Count; r++)
        {
            if (FoldHidden(r))
                continue;
            v++;
            if (v == vis)
                return r;
        }
        return _buf.Count - 1;
    }

    /// <summary>Locates a click in content coordinates: vis is the row from the text top, vc is the visual column.</summary>
    /// <summary>Shared single-segment list for the no-wrap path (read-only, never mutated).</summary>
    private static readonly List<int> SingleSegment = [0];

    internal static (int Row, int Col) LocateClick(
        IReadOnlyList<string> lines, SortedSet<int> folds,
        int top, int topSeg, bool wrap, int contentWidth, int left,
        int visTarget, int vcTarget)
    {
        int last = Math.Max(0, lines.Count - 1);
        int fileLine = top, firstSeg = wrap ? topSeg : 0, vis = 0;
        while (fileLine < lines.Count)
        {
            if (Folding.IsHidden(lines, folds, fileLine))
            {
                fileLine = Folding.EndOf(lines, FoldStartAt(lines, folds, fileLine)) + 1;
                firstSeg = 0;
                continue;
            }
            List<int> starts = wrap ? WordWrap.SegmentStarts(lines[fileLine], contentWidth) : SingleSegment;
            for (int s = firstSeg; s < starts.Count; s++)
            {
                if (vis == visTarget)
                {
                    int segStart = wrap ? starts[s] : left;
                    return (fileLine, ColumnAt(lines[fileLine], segStart + vcTarget));
                }
                vis++;
            }
            fileLine++;
            firstSeg = 0;
        }
        return (last, lines.Count == 0 ? 0 : lines[last].Length); // Below text means end
    }

    internal static int FoldStartAt(IReadOnlyList<string> lines, SortedSet<int> folds, int row)
    {
        int start = row;
        foreach (int f in folds)
        {
            if (f >= row)
                break;
            if (Folding.EndOf(lines, f) >= row)
                start = f;
        }
        return start;
    }

    /// <summary>Maps a visual column to a character index (tabs as in rendering).</summary>
    internal static int ColumnAt(string line, int target)
    {
        if (target <= 0)
            return 0;
        int vpos = 0, ci = 0;
        foreach (char c in line)
        {
            int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
            if (vpos + cw > target)
                break;
            vpos += cw;
            ci++;
        }
        return ci;
    }

    private void EnsureVisible(int textHeight, int contentWidth)
    {
        ClampCursor();
        if (FoldHidden(_row))
            UnfoldPath(); // Safety: cursor always lands on a visible row
        while (_top < _buf.Count - 1 && FoldHidden(_top))
        {
            _top++;
            _topSeg = 0;
        }
        bool wrap = _settings.WordWrap;
        SortedSet<int> folds = _docs[_active].Folds;
        if (!wrap)
        {
            _topSeg = 0;
            int vr = VisibleRow(_row), vt = VisibleRow(_top);
            if (vr < vt)
                vt = vr;
            if (vr >= vt + textHeight)
                vt = vr - textHeight + 1;
            _top = FileRowAt(vt);
            int vcol = TabStops.VisualWidth(_buf.GetLine(_row), _col);
            if (vcol < _left) _left = vcol;
            if (vcol >= _left + contentWidth) _left = vcol - contentWidth + 1;
            if (_top < 0) _top = 0;
            if (_left < 0) _left = 0;
            return;
        }
        _left = 0; // Wraps replace horizontal scrolling
        if (_row != _top) _topSeg = 0;
        if (_row < _top) { _top = _row; _topSeg = 0; }
        int rows = CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, true, textHeight, folds);
        if (rows < 0) // Cursor is above the visible area (shift inside a long row)
        {
            _top = _row; _topSeg = 0;
            rows = CursorVisualRow(_buf.Lines, _top, 0, _row, _col, contentWidth, true, textHeight, folds);
        }
        while (rows >= textHeight)
        {
            if (_top < _row)
            {
                _top++;
                _topSeg = 0;
            }
            else
            {
                // Cursor sits on a far segment of a long row - shows its tail.
                _topSeg = Math.Max(0, CursorSeg(_buf.GetLine(_row), _col, contentWidth) - textHeight + 1);
                break;
            }
            rows = CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, true, textHeight, folds);
        }
        if (_top < 0) { _top = 0; _topSeg = 0; }
    }

    internal static int CursorSeg(string line, int col, int contentWidth)
    {
        int vcol = TabStops.VisualWidth(line, col);
        return WordWrap.SegmentAt(WordWrap.SegmentStarts(line, contentWidth), vcol);
    }

    /// <summary>
    /// Computes the cursor visual row: segments [top, row) minus scrolled plus the cursor segment.
    /// Accuracy above cap is not guaranteed (cap=textHeight suffices for the screen).
    /// </summary>
    internal static int CursorVisualRow(IReadOnlyList<string> lines, int top, int topSeg,
        int row, int col, int contentWidth, bool wrap, int cap = int.MaxValue, SortedSet<int>? folds = null)
    {
        if (!wrap)
        {
            if (folds is null || folds.Count == 0)
                return row - top;
            int d = 0;
            for (int r = Math.Min(top, row); r < Math.Max(top, row); r++)
                if (!Folding.IsHidden(lines, folds, r))
                    d++;
            return row >= top ? d : -d;
        }
        int rows = -topSeg;
        for (int r = top; r < row && rows < cap; r++)
        {
            if (folds is not null && Folding.IsHidden(lines, folds, r))
                continue;
            rows += WordWrap.CountSegments(lines[r], contentWidth);
        }
        if (rows >= cap) return rows;
        return rows + CursorSeg(lines[row], col, contentWidth);
    }

    /// <summary>Whether a frame is due (pure, for tests).</summary>
    internal static bool ShouldRender(DateTime last, DateTime now) =>
        (now - last).TotalMilliseconds >= RenderThrottleMs;

    /// <summary>
    /// Paints unless a frame went out less than the throttle window ago: under a flood
    /// input keeps draining at full speed while paint caps at ~25fps. Slow frames are
    /// reported to the input log when diagnostics are on.
    /// </summary>
    private void RenderThrottled()
    {
        DateTime now = DateTime.UtcNow;
        if (!ShouldRender(_lastRenderAt, now))
            return;
        _lastRenderAt = now;
        InputLog.Paint();
        if (!InputLog.Enabled)
        {
            Render();
            return;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Render();
        sw.Stop();
        if (sw.ElapsedMilliseconds >= SlowFrameMs)
            InputLog.Frame(sw.ElapsedMilliseconds);
    }

    private void Render()
    {
        int w, h;
        try
        {
            w = Console.WindowWidth;
            h = Console.WindowHeight;
        }
        catch (IOException) { return; }
        if (w < 20 || h < 5)
        {
            try
            {
                Console.Clear();
                Console.ResetColor();
                Console.WriteLine(_loc["error.smallwindow"]);
            }
            catch { }
            return;
        }

        RenderFrame(w, h);
        Terminal.BeginSynchronizedUpdate();
        try
        {
            _screen.Flush();
            PlaceCursor();
        }
        finally
        {
            Terminal.EndSynchronizedUpdate();
        }
    }

    /// <summary>
    /// Renders one frame into the screen buffer without touching the console:
    /// size comes in as parameters, so tests can snapshot whole frames headless.
    /// </summary>
    /// <param name="w">The frame width in cells.</param>
    /// <param name="h">The frame height in cells.</param>
    internal void RenderFrame(int w, int h)
    {
        if (w < 20 || h < 5)
            return;
        int sideW = _sidebar is null ? 0 : SidebarState.Width;
        EditorLayout layout = EditorLayout.Compute(
            w, h, sideW, _panes.Count, _panes.Any(p => p.Docs.Count > 1));
        int[] paneWs = layout.PaneWs;
        int[] paneXs = layout.PaneXs;
        int tabH = layout.TabH;
        int textHeight = layout.TextHeight;
        int y0 = layout.Y0;
        bool wrap = _settings.WordWrap;
        int aNumWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
        int aGutter = _settings.ShowLineNumbers ? aNumWidth + 4 : 0;
        int activeCw = Math.Max(1, paneWs[_pane] - aGutter);

        EnsureVisible(textHeight, activeCw);
        if (_screen.Width != w || _screen.Height != h)
            _screen.Resize(w, h);

        // Menu bar (row 0).
        DrawMenuBar(w);

        // Inactive panes render from tab snapshots, the active one from fields (cache).
        int savedPane = _pane;
        SaveTabState();
        for (int i = 0; i < _panes.Count; i++)
        {
            _pane = i;
            LoadTabState();
            int px = paneXs[i], pw = paneWs[i];
            int numWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
            int gutterWidth = _settings.ShowLineNumbers ? numWidth + 4 : 0;
            int contentWidth = Math.Max(1, pw - gutterWidth);
            DrawTabs(px, pw, i == savedPane);
            DrawText(px, y0, px + pw, textHeight, contentWidth, gutterWidth, numWidth, wrap);
        }
        _pane = savedPane;
        LoadTabState();
        DrawSidebar();
        DrawPaneDividers(paneXs, y0, textHeight);

        string msg = CurrentMessage;
        string pos = _loc.Format("status.pos", _row + 1, _buf.Count, _col + 1) + (_buf.IsModified ? " *" : "");
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            pos += " | " + _loc.Format("status.sel", SelectionLength(sr, sc, er, ec));
        }
        string left = string.IsNullOrEmpty(msg) ? $" {pos}" : $" {msg}";
        string file = _buf.FilePath is null ? _loc["status.noname"] : Path.GetFileName(_buf.FilePath);
        if (_buf.IsReadOnly)
            file += " " + _loc["status.readonly"];
        string right = StatusBar.BuildRight(
            _buf.EncodingLabel, _buf.EndingLabel, _buf.IndentLabel, file,
            _git.StatusSegment(_buf.FilePath), _active, _docs.Count, _pane, _panes.Count);
        _screen.Text(0, h - 1, StatusBar.Build(left, right, w), _theme.StatusFg, _theme.StatusBg);

        // Over text: open menu and active dialog.
        DrawDropdown(w, h);
        if (_dialog is ModalDialog mdd)
        {
            mdd.HoverActive = _mouseActive;
            mdd.HoverButton = _mouseActive ? mdd.HitButton(_mouseX, _mouseY, w, h, _loc) : null;
        }
        _dialog?.Draw(_screen, _theme, _loc);
    }

    /// <summary>Places the hardware cursor (console only; frames stay headless).</summary>
    private void PlaceCursor()
    {
        int w = _screen.Width, h = _screen.Height;
        if (w < 20 || h < 5)
            return;
        int sideW = _sidebar is null ? 0 : SidebarState.Width;
        EditorLayout layout = EditorLayout.Compute(
            w, h, sideW, _panes.Count, _panes.Any(p => p.Docs.Count > 1));
        int[] paneWs = layout.PaneWs;
        int[] paneXs = layout.PaneXs;
        int textHeight = layout.TextHeight;
        int y0 = layout.Y0;
        bool wrap = _settings.WordWrap;
        int aNumWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
        int aGutter = _settings.ShowLineNumbers ? aNumWidth + 4 : 0;
        int activeCw = Math.Max(1, paneWs[_pane] - aGutter);
        bool uiOpen = _menu is not null || _dialog is not null || _sidebarFocus;
        string curLine = _buf.GetLine(_row);
        int vcolCur = TabStops.VisualWidth(curLine, _col);
        int curBase = wrap
            ? WordWrap.SegmentStarts(curLine, activeCw)[CursorSeg(curLine, _col, activeCw)]
            : _left;
        int pxA = paneXs[_pane], pwA = paneWs[_pane];
        int cx = pxA + aGutter + (vcolCur - curBase);
        int cy = y0 + CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, activeCw, wrap, textHeight,
            _docs[_active].Folds);
        (int x, int y)? dlgCursor = _dialog?.Cursor;
        bool pickerCursor = dlgCursor is not null;
        bool placed = pickerCursor
            || (!uiOpen && cy >= y0 && cy < y0 + textHeight && cx >= pxA + aGutter && cx < pxA + pwA);
        try
        {
            if (placed)
                Console.SetCursorPosition(pickerCursor ? dlgCursor!.Value.x : cx, pickerCursor ? dlgCursor!.Value.y : cy);
            Console.CursorVisible = placed;
        }
        catch { }
    }

    private CompiledGrammar? CurrentGrammar()
    {
        string g = _settings.Grammar;
        if (!string.IsNullOrWhiteSpace(g) && !string.Equals(g, "auto", StringComparison.OrdinalIgnoreCase))
        {
            CompiledGrammar? byName = GrammarRegistry.ByName(g);
            if (byName is not null)
                return byName;
        }
        string ext = string.Empty;
        try
        {
            ext = Path.GetExtension(_buf.FilePath ?? string.Empty);
        }
        catch
        {
        }
        return GrammarRegistry.ForExtension(ext);
    }

    /// <summary>
    /// Draws text character by character: cell color follows selection / search / current row / syntax.
    /// x0 offsets by pane width, y0 is the first text row.
    /// </summary>
    /// <summary>Gets the row number color: bookmark/fold gets accent, else git mark, else gutter.</summary>
    private Rgb GitGutterFg(
        (IReadOnlySet<int> added, IReadOnlySet<int> modified)? marks, int fileLine, int seg, bool pinned)
    {
        if (seg != 0)
            return _theme.GutterFg; // Wrapped-row continuation stays as before
        if (pinned)
            return _theme.AccentFg;
        if (marks is (var added, var modified))
        {
            if (added.Contains(fileLine))
                return _theme.GitAddFg;
            if (modified.Contains(fileLine))
                return _theme.GitModFg;
        }
        return _theme.GutterFg;
    }

    private void DrawText(int x0, int y0, int w, int textHeight, int contentWidth, int gutterWidth, int numWidth, bool wrap)
    {
        var gitMarks = _settings.GitGutter ? _git.DiffMarks(_buf.FilePath) : default((IReadOnlySet<int>, IReadOnlySet<int>)?);
        int y = y0;
        int fileLine = _top;
        int firstSeg = _topSeg;
        CompiledGrammar? grammar = CurrentGrammar();
        _docs[_active].Highlight.EnsurePrefetched(_buf, grammar);
        var bracket = BracketPair();
        var matchBudget = new MatchBudget(FrameMatchBudgetMs);
        while (y < y0 + textHeight && fileLine < _buf.Count)
        {
            if (FoldHidden(fileLine))
            {
                fileLine = Folding.EndOf(_buf.Lines, FoldStart(fileLine)) + 1;
                firstSeg = 0;
                continue;
            }
            string line = _buf.GetLine(fileLine);
            bool isCur = fileLine == _row;
            GetRowSelection(fileLine, line.Length, out int selA, out int selB);
            IReadOnlyList<SyntaxToken> synToks =
                _docs[_active].Highlight.GetLine(_buf, grammar, fileLine);
            int synIdx = 0;
            List<int> starts = wrap ? WordWrap.SegmentStarts(line, contentWidth) : SingleSegment;
            for (int s = firstSeg; s < starts.Count && y < y0 + textHeight; s++)
            {
                int segStart = starts[s];
                int segEnd = s + 1 < starts.Count ? starts[s + 1] : int.MaxValue;
                int @base = wrap ? segStart : _left;
                if (gutterWidth > 0)
                {
                    bool marked = s == 0 && _docs[_active].Bookmarks.Contains(fileLine);
                    bool folded = s == 0 && !marked && _docs[_active].Folds.Contains(fileLine)
                        && Folding.CanFold(_buf.Lines, fileLine);
                    string num = s == 0
                        ? (fileLine + 1).ToString(CultureInfo.InvariantCulture).PadLeft(numWidth)
                        : new string(' ', numWidth);
                    _screen.Text(x0, y, marked ? "●" : folded ? "▸" : " ",
                        marked || folded ? _theme.AccentFg : _theme.GutterFg, _theme.EditorBg);
                    _screen.Text(x0 + 1, y, num + " │ ",
                        GitGutterFg(gitMarks, fileLine, s, marked || folded),
                        _theme.EditorBg);
                }
                bool[] isMatch = FindMatches(TabStops.Slice(line, @base, contentWidth),
                    EffectiveSearchTerm, _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex, matchBudget);
                int vpos = 0;
                int ci = 0;
                bool leading = true;
                int guideStep = _buf.IndentString == "\t" ? TabStops.Width : Math.Max(1, _buf.IndentString.Length);
                foreach (char c in line)
                {
                    if (c != ' ' && c != '\t')
                        leading = false;
                    int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
                    if (vpos + cw <= segStart) { vpos += cw; ci++; continue; }
                    if (vpos >= segEnd) break;
                    for (int k = 0; k < cw; k++)
                    {
                        int vc = vpos + k - @base;
                        if (vc < 0)
                            continue;
                        if (vc >= contentWidth)
                            break;
                        bool sel = ci >= selA && ci < selB;
                        bool match = vc < isMatch.Length && isMatch[vc];
                        bool bmatch = bracket is not null
                            && ((fileLine == bracket.Value.Row && ci == bracket.Value.Col)
                                || (fileLine == bracket.Value.PairRow && ci == bracket.Value.PairCol));
                        while (synIdx + 1 < synToks.Count && synToks[synIdx + 1].Start <= ci)
                            synIdx++;
                        SyntaxToken tok = synIdx < synToks.Count ? synToks[synIdx] : default;
                        Rgb? synFg = ci >= tok.Start && ci < tok.Start + tok.Length
                            ? SyntaxHighlighter.ScopeColor(_theme, tok.Scope) : null;
                        (Rgb fg, Rgb bg) = (sel, match, bmatch, isCur, synFg) switch
                        {
                            (true, _, _, _, _) => (_theme.SelFg, _theme.SelBg),
                            (_, true, _, _, _) => (_theme.MatchFg, _theme.MatchBg),
                            (_, _, true, _, _) => (_theme.MatchFg, _theme.MatchBg),
                            (_, _, _, true, not null) => (synFg!.Value, _theme.CurLineBg),
                            (_, _, _, true, _) => (_theme.CurLineFg, _theme.CurLineBg),
                            (_, _, _, _, not null) => (synFg!.Value, _theme.EditorBg),
                            _ => (_theme.EditorFg, _theme.EditorBg),
                        };
                        if (_settings.RulerColumn > 0 && !sel && !match && !bmatch
                            && vpos + k == _settings.RulerColumn - 1)
                            bg = _theme.RulerBg;
                        char glyph = c == '\t' ? ' ' : c;
                        if (_settings.ShowIndentGuides && leading && (c == ' ' || c == '\t')
                            && k == 0 && !sel && !match && !bmatch && vpos % guideStep == 0)
                        {
                            glyph = '│';
                            fg = _theme.IndentGuideFg;
                        }
                        else if (_settings.ShowWhitespace && !sel && !match && !bmatch)
                        {
                            if (c == ' ')
                            {
                                glyph = '·';
                                fg = _theme.FillerFg;
                            }
                            else if (c == '\t' && k == 0)
                            {
                                glyph = '→';
                                fg = _theme.FillerFg;
                            }
                        }
                        _screen.Set(x0 + gutterWidth + vc, y, glyph, fg, bg);
                    }
                    vpos += cw;
                    if (vpos - @base >= contentWidth)
                        break;
                    ci++;
                }
                // Pads the row tail with spaces in the normal color.
                int filled = Math.Clamp(vpos - @base, 0, contentWidth);
                (Rgb tailFg, Rgb tailBg) = isCur
                    ? (_theme.CurLineFg, _theme.CurLineBg)
                    : (_theme.EditorFg, _theme.EditorBg);
                _screen.Fill(x0 + gutterWidth + filled, y, contentWidth - filled, ' ', tailFg, tailBg);
                y++;
            }
            fileLine++;
            firstSeg = 0;
        }
        while (y < y0 + textHeight)
        {
            _screen.Text(x0, y, ("~".PadRight(w - x0))[..(w - x0)], _theme.FillerFg, _theme.EditorBg);
            y++;
        }
    }

    /// <summary>
    /// Draws the file panel on the left, full height (menu bar excluded, status bar
    /// excluded): the tab row starts right of it. Header plus scrolling list.
    /// Colors by kind: folders, hidden, executables, files.
    /// </summary>
    private void DrawSidebar()
    {
        if (_sidebar is null)
            return;
        int sw = SidebarState.Width;
        int inner = sw - 1;
        int totalRows = Math.Max(1, _screen.Height - 2); // rows 1..h-2
        string title = "▸ " + Path.GetFileName(
            _sidebar.CurrentDir.TrimEnd(Path.DirectorySeparatorChar));
        if (title.Length > inner)
            title = "…" + title[^(inner - 1)..];
        _screen.Text(0, 1, Dialog.FitCell(title, inner) + "│", _theme.MenuOpenFg, _theme.MenuOpenBg);
        int visCount = Math.Max(1, totalRows - 1);
        var rows = _sidebar.Rows;
        int vis = Math.Min(visCount, rows.Count - _sidebar.Top);
        for (int vi = 0; vi < vis; vi++)
        {
            int i = _sidebar.Top + vi;
            var (node, depth) = rows[i];
            string marker = node.IsDir ? (node.IsExpanded ? "▾ " : "▸ ") : "  ";
            string label = new string(' ', Math.Min(depth, 8) * 2) + marker + node.Name;
            var kind = new SidebarEntry(node.Name, node.IsDir, node.IsHidden, node.IsExe);
            if (label.Length > inner)
                label = label[..(inner - 1)] + "…";
            if (vi == 0 && _sidebar.Top > 0)
                label = label[..^1] + "↑";
            if (vi == vis - 1 && _sidebar.Top + vis < rows.Count)
                label = label[..^1] + "↓";
            string cell = Dialog.FitCell(label, inner) + "│";
            int row = 1 + 1 + vi;
            if (i == _sidebar.Selected)
                _screen.Text(0, row, cell, _theme.ButtonSelFg, _theme.ButtonSelBg);
            else
                _screen.Text(0, row, cell, SidebarRowFg(_theme, kind, node), _theme.EditorBg);
        }
        for (int row = 1 + 1 + vis; row < 1 + totalRows; row++)
            _screen.Text(0, row, new string(' ', inner) + "│", _theme.EditorFg, _theme.EditorBg);
    }

    /// <summary>Gets the sidebar row color: git marks win (when enabled), then kind.</summary>
    private Rgb SidebarRowFg(Theme theme, SidebarEntry kind, SidebarNode node)
    {
        if (_settings.GitGutter && _sidebar is SidebarState sidebar)
        {
            var (added, modified) = sidebar.GitMark(node);
            if (added)
                return theme.GitAddFg;
            if (modified)
                return theme.GitModFg;
        }
        return EntryFg(theme, kind);
    }

    /// <summary>Gets the sidebar entry color by kind (the selected entry paints separately).</summary>
    internal static Rgb EntryFg(Theme theme, SidebarEntry e)
    {
        if (e.Name == "..")
            return theme.PickerUpFg;
        if (e.IsHidden)
            return theme.PickerHiddenFg;
        if (e.IsExe)
            return theme.PickerExeFg;
        if (e.IsDir)
            return theme.PickerDirFg;
        return theme.EditorFg;
    }

    /// <summary>
    /// Draws the tab row in a pane region: highlights the active tab, marks dirty tabs with "*",
    /// windows long rows (the active tab stays visible). Dims foreign panes.
    /// </summary>
    private void DrawTabs(int px, int pw, bool focused)
    {
        if (_docs.Count <= 1 || pw <= 0)
            return;
        var cells = new List<string>();
        var widths = new List<int>();
        for (int i = 0; i < _docs.Count; i++)
        {
            string seg = (i > 0 ? "│" : string.Empty) + $" {_docs[i].TabTitle(_loc)} ";
            cells.Add(seg);
            widths.Add(seg.Length);
        }
        _tabLeft = Math.Clamp(TabWindowStart(widths, _active, _tabLeft, pw), 0, _docs.Count - 1);
        var row = new System.Text.StringBuilder();
        var spans = new List<(int x, int len, bool active)>();
        int shownLast = _tabLeft - 1;
        for (int i = _tabLeft; i < _docs.Count && row.Length < pw; i++)
        {
            string seg = cells[i];
            if (row.Length + seg.Length > pw)
                break;
            spans.Add((row.Length, seg.Length, i == _active));
            row.Append(seg);
            shownLast = i;
        }
        string text = row.ToString();
        if (_tabLeft > 0 && text.Length > 0)
            text = "‹" + text[1..];
        if (shownLast < _docs.Count - 1 && text.Length > 0)
            text = text[..^1] + "›";
        Rgb rowFg = focused ? _theme.EditorFg : _theme.DropDimFg;
        _screen.Text(px, 1, text.PadRight(pw)[..pw], rowFg, _theme.EditorBg);
        foreach (var (x, len, active) in spans)
        {
            if (!active || x >= pw)
                continue;
            int show = Math.Min(len, pw - x);
            _screen.Text(px + x, 1, text.Substring(x, show), _theme.ButtonSelFg, _theme.ButtonSelBg);
        }
    }

    private void DrawPaneDividers(int[] paneXs, int y0, int textHeight)
    {
        for (int i = 1; i < paneXs.Length; i++)
            for (int y = 1; y < y0 + textHeight; y++)
                _screen.Set(paneXs[i], y, '│', _theme.GutterFg, _theme.EditorBg);
    }

    /// <summary>Computes the visible tab window start: shifts until the active tab fits.</summary>
    internal static int TabWindowStart(IReadOnlyList<int> widths, int active, int start, int w)
    {
        if (widths.Count == 0)
            return 0;
        active = Math.Clamp(active, 0, widths.Count - 1);
        start = Math.Clamp(start, 0, active);
        while (start <= active)
        {
            int used = 0;
            for (int i = start; i <= active; i++)
                used += widths[i];
            if (used <= w)
                return start;
            start++;
        }
        return active;
    }

    private void GetRowSelection(int fileLine, int lineLen, out int a, out int b)
    {
        a = 0;
        b = 0;
        if (!_sel.HasSelection(_row, _col))
            return;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        if (fileLine < sr || fileLine > er)
            return;
        a = Math.Min(fileLine == sr ? sc : 0, lineLen);
        b = Math.Min(fileLine == er ? ec : lineLen, lineLen);
    }

    /// <summary>Per-row regex timeout for live highlight (search ops keep the generous one).</summary>
    private static readonly TimeSpan HighlightRegexTimeout = TimeSpan.FromMilliseconds(25);

    /// <summary>Shared per-frame budget for match highlighting (catastrophic patterns).</summary>
    internal const long FrameMatchBudgetMs = 100;

    private static bool[] FindMatches(string expanded, string term, bool matchCase, bool wholeWord, bool useRegex, MatchBudget? budget = null)
    {
        bool[] m = new bool[expanded.Length];
        if (string.IsNullOrEmpty(term))
            return m;
        if (budget?.Exhausted == true)
            return m; // budget spent on rows above: skip, don't stall the frame
        if (useRegex)
        {
            Regex? rx = TextBuffer.TryBuildRegex(term, matchCase, wholeWord, HighlightRegexTimeout);
            if (rx is null)
                return m;
            try
            {
                foreach (Match mt in rx.Matches(expanded))
                {
                    int len = Math.Max(1, mt.Length);
                    for (int j = mt.Index; j < mt.Index + len && j < m.Length; j++)
                        m[j] = true;
                }
            }
            catch (RegexMatchTimeoutException) { }
            return m;
        }
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int p = 0;
        while (p <= expanded.Length - term.Length &&
            (p = expanded.IndexOf(term, p, expanded.Length - p, cmp)) >= 0)
        {
            if (!wholeWord || IsWhole(expanded, p, term.Length))
                for (int j = p; j < p + term.Length && j < m.Length; j++)
                    m[j] = true;
            p++;
        }
        return m;
    }

    private static bool IsWhole(string s, int idx, int len) =>
        (idx == 0 || !(char.IsLetterOrDigit(s[idx - 1]) || s[idx - 1] == '_')) &&
        (idx + len >= s.Length || !(char.IsLetterOrDigit(s[idx + len]) || s[idx + len] == '_'));

    private void DrawMenuBar(int w)
    {
        List<TopMenu> menus = _menu?.Menus ?? BuildMenus(_loc);
        _menuX.Clear();
        int x = 0;
        for (int i = 0; i < menus.Count; i++)
        {
            _menuX.Add(x);
            string cell = $" {menus[i].Label} ";
            if (x + cell.Length > w)
                break;
            if (_menu is not null && _menu.OpenIndex == i)
            {
                _screen.Text(x, 0, cell, _theme.MenuOpenFg, _theme.MenuOpenBg);
            }
            else
            {
                _screen.Text(x, 0, " ", _theme.MenuFg, _theme.MenuBarBg);
                WriteMenuCell(x + 1, menus[i].Label, menus[i].Hotkey);
                _screen.Text(x + cell.Length - 1, 0, " ", _theme.MenuFg, _theme.MenuBarBg);
            }
            x += cell.Length;
        }
        string name = _buf.FilePath ?? _loc["status.noname"];
        string dirty = _buf.IsModified ? "*" : string.Empty;
        string right = $" {name}{dirty} ";
        int rest = w - x;
        switch (rest, right.Length >= rest)
        {
            case ( <= 0, _):
                break;
            case (_, true):
                _screen.Text(x, 0, right[..rest], _theme.MenuFg, _theme.MenuBarBg);
                break;
            default:
                _screen.Text(x, 0, new string(' ', rest - right.Length) + right, _theme.MenuFg, _theme.MenuBarBg);
                break;
        }
    }

    private void WriteMenuCell(int x, string label, char hotkey)
    {
        int idx = label.IndexOf(char.ToUpperInvariant(hotkey));
        if (idx < 0)
            idx = label.IndexOf(char.ToLowerInvariant(hotkey));
        if (idx < 0)
        {
            _screen.Text(x, 0, label, _theme.MenuFg, _theme.MenuBarBg);
            return;
        }
        _screen.Text(x, 0, label[..idx], _theme.MenuFg, _theme.MenuBarBg);
        _screen.Text(x + idx, 0, label[idx..(idx + 1)], _theme.MenuHotkeyFg, _theme.MenuBarBg);
        _screen.Text(x + idx + 1, 0, label[(idx + 1)..], _theme.MenuFg, _theme.MenuBarBg);
    }

    private void DrawDropdown(int w, int h)
    {
        if (_menu is null || _menuX.Count == 0)
            return;
        TopMenu m = _menu.Current;
        int x = _menuX[Math.Min(_menu.OpenIndex, _menuX.Count - 1)];
        int inner = 0;
        foreach (MenuItem it in m.Items)
        {
            string rc = it.Shortcut ?? it.Hotkey.ToString();
            inner = Math.Max(inner, 1 + it.Label.Length + 2 + rc.Length + 1);
        }
        int boxW = Math.Min(inner + 2, w - x);
        if (boxW < 10 || x >= w)
            return;
        int y = 1;
        int maxRows = h - 1 - y;
        if (maxRows < 3)
            return;
        int rows = Math.Min(m.Items.Count, maxRows - 2);
        // Hover inside the box owns the highlight (a separator row lights nothing);
        // outside it the keyboard selection stays visible instead of jumping to item 0.
        int r = _mouseY - (y + 1);
        bool mouseInBox = _mouseActive && _mouseX >= x && _mouseX < x + boxW && r >= 0 && r < rows;
        int hoverRow = -1;
        if (mouseInBox && !m.Items[r].IsSeparator)
            hoverRow = r;

        Rgb borderFg = _theme.DropBorderFg;
        Rgb borderBg = _theme.DropBg;
        _screen.Text(x, y, "┌" + new string('─', boxW - 2) + "┐", borderFg, borderBg);
        for (int i = 0; i < rows; i++)
        {
            MenuItem it = m.Items[i];
            if (it.IsSeparator)
            {
                _screen.Text(x, y + 1 + i, "├" + new string('─', boxW - 2) + "┤", borderFg, borderBg);
                continue;
            }
            int effRow = mouseInBox ? hoverRow : _menu.SelectedIndex;
            bool sel = i == effRow;
            (Rgb fg, Rgb bg) = sel
                ? (_theme.DropSelFg, _theme.DropSelBg)
                : (_theme.DropFg, _theme.DropBg);
            _screen.Text(x, y + 1 + i, "│", borderFg, borderBg);
            string right = " " + (it.Shortcut ?? it.Hotkey.ToString());
            int room = boxW - 2 - right.Length - 1;
            string label = " " + (it.Label.Length > room ? it.Label[..Math.Max(0, room)] : it.Label);
            _screen.Text(x + 1, y + 1 + i, label.PadRight(boxW - 2 - right.Length), fg, bg);
            _screen.Text(x + 1 + boxW - 2 - right.Length, y + 1 + i, right,
                sel ? fg : _theme.DropDimFg, bg);
            _screen.Text(x + boxW - 1, y + 1 + i, "│", borderFg, borderBg);
        }
        _screen.Text(x, y + 1 + rows, "└" + new string('─', boxW - 2) + "┘", borderFg, borderBg);
    }

    private void RunSettings()
    {
        RunDialog(new SettingsDialog(_settings, _store, ApplySettings));
    }

    private string OnOff(bool v) => v ? _loc["settings.on"] : _loc["settings.off"];
}

/// <summary>
/// Shared per-frame budget for search-match highlighting: a catastrophic regex
/// must stall rows, never the frame. Rows past the budget render unhighlighted.
/// </summary>
internal sealed class MatchBudget
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly long _capMs;

    internal MatchBudget(long capMs) => _capMs = capMs;

    internal bool Exhausted => _sw.ElapsedMilliseconds >= _capMs;
}
