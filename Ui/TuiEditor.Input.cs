using System.Globalization;

namespace TuiEdit;

/// <summary>TuiEditor: input dispatch, menus, pickers and prompts.</summary>
internal sealed partial class TuiEditor
{
    private void HandleInput(InputEvent input)
    {
        if (input is PasteInput paste)
        {
            if (_dialog is not null)
            {
                _dialog.Paste(paste.Text); // Manager inserts, others ignore
                return;
            }
            _menu = null;
            InsertPastedText(paste.Text + DrainPasteRun(string.Join("\n", _clipboard)));
            return;
        }
        if (input is MouseInput mouse)
        {
            HandleMouse(mouse);
            return;
        }
        _mouseActive = false; // Keyboard/paste clears hover
        _mouseDrag = false; // Key mid-drag stops the drag, keeps selection as-is
        if (input is KeyInput key)
            HandleKey(key.Key);
        // Ignores future event types instead of casting (mouse already broke this spot once).
    }

    /// <summary>Handles the mouse: reads sizes from the console, then runs pure logic.</summary>
    private void HandleMouse(MouseInput m)
    {
        int w, h;
        try
        {
            w = Console.WindowWidth;
            h = Console.WindowHeight;
        }
        catch
        {
            return;
        }
        HandleMouseGuarded(m, w, h);
    }

    /// <summary>
    /// Mouse entry with a session fallback: reports come from flaky terminal layers,
    /// so a throwing mouse path parks the mouse for the rest of the session
    /// instead of taking the editor down. Never throws.
    /// </summary>
    internal void HandleMouseGuarded(MouseInput m, int w, int h)
    {
        try
        {
            HandleMouseAt(m, w, h);
        }
        catch (Exception ex)
        {
            CrashLog.Write("mouse", ex);
            _settings.Mouse = MouseLevel.Off;
            ApplyMouseSetting();
            SetMessage(_loc["error.mouseoff"]);
        }
    }

    /// <summary>
    /// Handles the mouse: motion is hover-only; modals take buttons/wheel/background-close;
    /// menus take buttons; tabs/panes take focus; stray wheel scrolls the active pane.
    /// Priority: modal -&gt; dialog -&gt; menu -&gt; bar -&gt; tabs/text.
    /// </summary>
    internal void HandleMouseAt(MouseInput m, int w, int h)
    {
        if (!InputReader.MouseEnabled)
            return;
        _mouseActive = true;
        _mouseX = m.X;
        _mouseY = m.Y;
        if (m.Action == MouseAction.Move)
        {
            // Release (no button) with an armed click: the press armed it, the release decides.
            if (m.Button == MouseButton.None && (_armedDialog is not null || _armedMenu is not null))
            {
                ReleaseMouse(m, w, h);
                return;
            }
            // Hover tracks position only; drag with held left extends, release finishes.
            if (_mouseDrag && m.Button == MouseButton.Left)
                ExtendMouseDrag(m, w, h);
            else if (_mouseDrag)
                EndMouseDrag(copy: true);
            return;
        }
        _mouseDrag = false; // Any non-motion without viewport-press disarms the drag
        DisarmMouse(); // A fresh press replaces any armed click (re-armed below when applicable)
        if (_dialog is ModalDialog md)
        {
            if (m.Action is MouseAction.WheelUp or MouseAction.WheelDown)
            {
                int dir = m.Action == MouseAction.WheelDown ? 1 : -1;
                for (int i = 0; i < m.Count; i++)
                    md.ScrollList(dir);
                return;
            }
            if (m.Action != MouseAction.LeftPress)
                return; // Middle/right have no consumers
            int? hit = md.HitButton(m.X, m.Y, w, h, _loc);
            if (hit is null)
            {
                // Missed the box - acts as Esc: cancels without outcome (was a focus trap).
                md.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
            }
            else
            {
                // Arm only: releasing on the same button fires (dragging off cancels).
                _armedDialog = md;
                _armedButton = hit.Value;
            }
            if (ReferenceEquals(_dialog, md) && md.Closed)
                _dialog = null; // Matches the keyboard path: clears an outcome without a new dialog
            return;
        }
        if (_dialog is not null)
            return; // RunDialog dialogs (settings, manager): no mouse yet
        if (m.Action == MouseAction.RightPress && _menu is not null)
            return; // Ignores right-click on menus, items click with left only
        if (_menu is not null && HandleMenuMouse(m, w, h))
            return;
        // Missed the menu (closed) - lets the click reach text, like a key in HandleMenuKey.
        if (w < 20 || h < 5)
            return;
        if (_menu is null && m.Y == 0 && m.Action == MouseAction.LeftPress)
        {
            // Closed menu: a bar click opens it (y=0 never hits text).
            int? bar = MenuHit.BarHit(BuildMenus(_loc), m.X, w);
            if (bar is not null)
            {
                OpenMenu(bar.Value);
                return;
            }
        }
        int sideW = _sidebar is null ? 0 : SidebarState.Width;
        EditorLayout layout = EditorLayout.Compute(
            w, h, sideW, _panes.Count, _panes.Any(p => p.Docs.Count > 1));
        if (m.Action is MouseAction.WheelUp or MouseAction.WheelDown)
        {
            // Wheel outside modal/menu always scrolls the active pane,
            // even over gutter, sidebar, and status bar (fallback).
            ScrollWheel(m.Action == MouseAction.WheelDown ? 1 : -1, m.Count);
            return;
        }
        if (m.Action is not (MouseAction.LeftPress or MouseAction.RightPress))
            return; // Middle has no consumers
        if (layout.TabH == 1 && m.Y == 1)
        {
            if (m.Action != MouseAction.LeftPress)
                return;
            int tp = layout.PaneAt(m.X);
            if (tp < 0)
                return;
            if (tp != _pane)
                SwitchPane(tp);
            if (_docs.Count > 1)
            {
                int? tab = TabHit(
                    _docs.Select(d => d.TabTitle(_loc)).ToList(),
                    _active, _tabLeft, layout.PaneWs[tp], m.X - layout.PaneXs[tp]);
                if (tab is not null)
                    SwitchTab(tab.Value);
            }
            return;
        }
        int pane = layout.PaneAt(m.X);
        if (pane < 0)
            return; // Sidebar: no clicks
        if (pane != _pane)
            SwitchPane(pane); // Click in another pane focuses it first
        var g = TextGeom(layout);
        if (m.X < g.cx0 || m.X >= g.x1 || m.Y < g.y0 || m.Y >= g.y1)
            return; // Gutter/divider/status bar: no clicks
        if (m.Action == MouseAction.RightPress)
        {
            CopyMouseSelection(); // Copies a selection to the buffer, ignores none
            return;
        }
        (int row, int col) = LocateClick(_buf.Lines, _docs[_active].Folds, _top, _topSeg,
            _settings.WordWrap, g.cw, _left, m.Y - g.y0, m.X - g.cx0);
        _sel.Clear();
        _row = row;
        _col = col;
        ClampCursor();
        TrackCol();
        if (m.Action == MouseAction.LeftPress && IsDoubleClick(m.X, m.Y, DateTime.UtcNow))
        {
            SelectWordAt(_row, _col);
            return; // word selected: no drag anchor
        }
        _mouseDrag = true; // Press in text: drag anchor
        _mousePane = pane;
        _mouseRow = _row;
        _mouseCol = _col;
    }

    /// <summary>Same cell within the double-click window (updates the anchor either way).</summary>
    internal bool IsDoubleClick(int x, int y, DateTime now)
    {
        bool dbl = x == _lastPressX && y == _lastPressY && (now - _lastPressAt) <= DoubleClickWindow;
        _lastPressX = x;
        _lastPressY = y;
        _lastPressAt = now;
        return dbl;
    }

    /// <summary>Selects the word under the cursor (double-click); no word — plain click stands.</summary>
    private void SelectWordAt(int row, int col)
    {
        if (row < 0 || row >= _buf.Count)
            return;
        string line = _buf.Lines[row];
        int start = Math.Min(col, line.Length), end = start;
        while (start > 0 && TextBuffer.IsWordChar(line[start - 1]))
            start--;
        while (end < line.Length && TextBuffer.IsWordChar(line[end]))
            end++;
        if (start == end)
            return;
        _sel.Clear();
        _sel.Start(row, start);
        _row = row;
        _col = end;
        ClampCursor();
        TrackCol();
    }

    /// <summary>Gets the active pane text geometry (gutter follows its buffer).</summary>
    private (int cx0, int x1, int y0, int y1, int cw) TextGeom(EditorLayout layout)
    {
        int numWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
        int gutterWidth = _settings.ShowLineNumbers ? numWidth + 4 : 0;
        int cw = Math.Max(1, layout.PaneWs[_pane] - gutterWidth);
        int cx0 = layout.PaneXs[_pane] + gutterWidth;
        return (cx0, layout.PaneXs[_pane] + layout.PaneWs[_pane], layout.Y0,
            layout.Y0 + layout.TextHeight, cw);
    }

    /// <summary>Extends the drag selection from the press anchor to the mouse position.</summary>
    private void ExtendMouseDrag(MouseInput m, int w, int h)
    {
        if (_mousePane < 0 || _mousePane >= _panes.Count || _mousePane != _pane)
        {
            _mouseDrag = false; // Keys moved focus mid-drag - stops
            return;
        }
        EditorLayout layout = EditorLayout.Compute(
            w, h, _sidebar is null ? 0 : SidebarState.Width,
            _panes.Count, _panes.Any(p => p.Docs.Count > 1));
        var g = TextGeom(layout);
        int vx = Math.Clamp(m.Y - g.y0, 0, Math.Max(0, g.y1 - g.y0 - 1));
        int vc = Math.Clamp(m.X - g.cx0, 0, Math.Max(0, g.x1 - g.cx0 - 1));
        (int row, int col) = LocateClick(_buf.Lines, _docs[_active].Folds, _top, _topSeg,
            _settings.WordWrap, g.cw, _left, vx, vc);
        if (!_sel.Active)
            _sel.Start(_mouseRow, _mouseCol); // First move anchors the press
        _row = row;
        _col = col;
        ClampCursor();
        TrackCol();
    }

    /// <summary>Releases after a drag: applies the copy policy with a selection, clears without one.</summary>
    private void EndMouseDrag(bool copy)
    {
        _mouseDrag = false;
        if (!_sel.HasSelection(_row, _col))
        {
            _sel.Clear();
            return;
        }
        if (copy && _settings.CopyOnSelect)
            CopyMouseSelection(); // Copies and clears
        // Otherwise the selection stays for keyboard operations.
    }

    /// <summary>Copies the selection to the internal buffer plus OSC 52, with a message; ignores empty selections.</summary>
    private void CopyMouseSelection()
    {
        if (!_sel.HasSelection(_row, _col))
            return;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        StoreYank(_buf.GetRangeText(sr, sc, er, ec).ToList(), isDelete: false);
        _clipboardSvc.Export(_clipboard);
        SetMessage(_loc.Format("msg.copy.sel", SelectionLength(sr, sc, er, ec)));
        _sel.Clear();
    }

    /// <summary>
    /// Hits the tab under X inside a pane (mirrors DrawTabs).
    /// Returns the index or null (miss/single tab).
    /// </summary>
    internal static int? TabHit(
        IReadOnlyList<string> titles, int active, int tabLeft, int paneW, int x)
    {
        if (titles.Count <= 1 || x < 0 || x >= paneW)
            return null;
        int[] widths = new int[titles.Count];
        for (int i = 0; i < titles.Count; i++)
            widths[i] = titles[i].Length + 4;
        int tl = Math.Clamp(TabWindowStart(widths, active, tabLeft, paneW), 0, titles.Count - 1);
        int xx = 0;
        for (int i = tl; i < titles.Count && xx < paneW; i++)
        {
            if (xx + widths[i] > paneW)
                break;
            if (x >= xx && x < xx + widths[i])
                return i;
            xx += widths[i];
        }
        return null;
    }

    /// <summary>
    /// Handles mouse with an open menu: bar opens/switches, dropdown picks an item, miss closes.
    /// Returns false when a stray click closed the menu (the click reaches text).
    /// </summary>
    private bool HandleMenuMouse(MouseInput m, int w, int h)
    {
        if (_menu is null)
            return true;
        if (m.Action != MouseAction.LeftPress)
        {
            _menu = null; // Wheel and others miss, closes
            return true;
        }
        List<TopMenu> menus = _menu.Menus;
        if (m.Y == 0)
        {
            int? hit = MenuHit.BarHit(menus, m.X, w);
            int was = _menu.OpenIndex;
            _menu = null;
            if (hit is not null && hit.Value != was)
                OpenMenu(hit.Value); // Switches to another menu
            // Repeat on own / miss on cells stays closed
            return true;
        }
        int? item = MenuItemHit(_menu, m.X, m.Y, w, h);
        if (item is null)
        {
            if (MenuHit.IsSeparatorHit(_menu.Current, MenuHit.MenuX(_menu), m.X, m.Y, w, h))
                return true; // separator: dead zone, menu stays open
            _menu = null;
            return false; // Miss closes and passes the click to text
        }
        // Arm only: releasing over an item fires it (allows press-drag-release).
        _armedMenu = _menu;
        return true;
    }

    /// <summary>Clears an armed press-release click (a new press or wheel replaces it).</summary>
    private void DisarmMouse()
    {
        _armedDialog = null;
        _armedButton = -1;
        _armedMenu = null;
    }

    /// <summary>Hits the dropdown item under coordinates (null outside).</summary>
    private static int? MenuItemHit(MenuState menu, int x, int y, int w, int h) =>
        MenuHit.DropdownHit(menu.Current, MenuHit.MenuX(menu), x, y, w, h);

    /// <summary>
    /// Finishes an armed click: a release on the armed dialog button fires it,
    /// a release over a menu item fires that item (press-drag-release slides);
    /// releasing anywhere else just disarms (menu closes without touching text).
    /// Stale arms (dialog/menu gone meanwhile) are swallowed quietly.
    /// </summary>
    private void ReleaseMouse(MouseInput m, int w, int h)
    {
        Dialog? dlg = _armedDialog;
        int btn = _armedButton;
        MenuState? menu = _armedMenu;
        DisarmMouse();
        if (dlg is ModalDialog md && ReferenceEquals(_dialog, md))
        {
            if (md.HitButton(m.X, m.Y, w, h, _loc) == btn)
            {
                md.HandleClick(m.X, m.Y, w, h, _loc);
                if (ReferenceEquals(_dialog, md) && md.Closed)
                    _dialog = null;
            }
            return;
        }
        if (menu is not null && ReferenceEquals(_menu, menu))
        {
            int? item = MenuItemHit(menu, m.X, m.Y, w, h);
            if (item is not null)
                ActivateMenuItem(menu.Current.Items[item.Value]);
            else if (!MenuHit.IsSeparatorHit(menu.Current, MenuHit.MenuX(menu), m.X, m.Y, w, h))
                _menu = null; // off-menu: close; separator: disarm only, menu stays
        }
    }

    /// <summary>Scrolls on wheel: moves the cursor +-3 rows per tick (steps merges a tick batch).</summary>
    private void ScrollWheel(int dir, int steps = 1)
    {
        if (_buf.Count == 0)
            return;
        _sel.Clear();
        _row = Math.Clamp(_row + 3 * dir * Math.Max(1, steps), 0, Math.Max(0, _buf.Count - 1));
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        ClampCursor();
        TrackCol();
    }

    private void HandleKey(ConsoleKeyInfo k)
    {
        // An open dialog swallows all input.
        if (_dialog is not null)
        {
            Dialog d = _dialog;
            d.HandleKey(k);
            if (ReferenceEquals(_dialog, d) && d.Closed)
                _dialog = null;
            return;
        }

        // An open menu intercepts input.
        if (_menu is not null)
        {
            HandleMenuKey(k);
            return;
        }

        // Focus in the file panel: the panel swallows navigation and Enter.
        if (_sidebarFocus && _sidebar is not null)
        {
            HandleSidebarKey(k);
            return;
        }

        // Esc clears the active selection and any extra carets.
        if (k.Key == ConsoleKey.Escape && (_sel.HasSelection(_row, _col) || HasExtraCarets))
        {
            _sel.Clear();
            ClearExtraCarets();
            return;
        }

        EditorCommand cmd = _keys.Map(k);

        // Held Ctrl+V outruns the frame: merge the whole pending run into one
        // insertion, otherwise repeats keep pasting after key release.
        if (cmd == EditorCommand.Paste)
        {
            CoalescedKeyPaste();
            return;
        }

        if (IsMovement(cmd))
        {
            bool extend = (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (extend && !_sel.HasSelection(_row, _col))
                _sel.Start(_row, _col);
            if (extend && HasExtraCarets)
                ClearExtraCarets(); // shift-selection collapses to the primary caret
            if (!extend && HasExtraCarets && cmd is EditorCommand.MoveLeft
                or EditorCommand.MoveRight or EditorCommand.MoveUp or EditorCommand.MoveDown
                or EditorCommand.GoHome or EditorCommand.GoEnd
                or EditorCommand.WordLeft or EditorCommand.WordRight
                or EditorCommand.PageUp or EditorCommand.PageDown
                or EditorCommand.GoDocStart or EditorCommand.GoDocEnd)
            {
                int page = Math.Max(1, TextHeight() - 1);
                MoveAllCarets(cmd switch
                {
                    EditorCommand.MoveLeft => StepLeft,
                    EditorCommand.MoveRight => StepRight,
                    EditorCommand.MoveUp => StepUp,
                    EditorCommand.MoveDown => StepDown,
                    EditorCommand.GoHome => ((int r, int c) => (r, 0)),
                    EditorCommand.WordLeft => StepWordLeft,
                    EditorCommand.WordRight => StepWordRight,
                    EditorCommand.PageUp => ((int r, int c) => StepPage(page, -1, r, c)),
                    EditorCommand.PageDown => ((int r, int c) => StepPage(page, 1, r, c)),
                    EditorCommand.GoDocStart => ((int r, int c) => (0, 0)),
                    EditorCommand.GoDocEnd => ((int r, int c) => (_buf.Count - 1, _buf.GetLine(_buf.Count - 1).Length)),
                    _ => ((int r, int c) => (r, _buf.GetLine(Math.Clamp(r, 0, _buf.Count - 1)).Length)),
                });
                return;
            }
            Execute(cmd, k);
            if (!extend)
                _sel.Clear();
            return;
        }

        Execute(cmd, k);
    }

    /// <summary>Gets cursor movement commands (participate in Shift-selection).</summary>
    private static bool IsMovement(EditorCommand cmd) => cmd switch
    {
        EditorCommand.MoveLeft or EditorCommand.MoveRight
            or EditorCommand.MoveUp or EditorCommand.MoveDown
            or EditorCommand.GoHome or EditorCommand.GoEnd
            or EditorCommand.GoDocStart or EditorCommand.GoDocEnd
            or EditorCommand.PageUp or EditorCommand.PageDown
            or EditorCommand.WordLeft or EditorCommand.WordRight => true,
        _ => false,
    };

    /// <summary>
    /// Tracks wire-speed printable runs (terminal paste without bracketed markers,
    /// PSReadLine-style speed inference): returns true from the 4th consecutive
    /// sub-20ms char. Anything else resets. The InsertChar handler uses it to skip
    /// AutoPair and merge undos; instant echo is preserved (no holding).
    /// </summary>
    private bool TrackSpeedRun(ConsoleKeyInfo k)
    {
        if (char.IsControl(k.KeyChar) || (k.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
        {
            _speedCount = 0;
            return false;
        }
        DateTime now = DateTime.UtcNow;
        _speedCount = (now - _speedLastAt).TotalMilliseconds < SpeedRunGapMs ? _speedCount + 1 : 1;
        _speedLastAt = now;
        return _speedCount >= SpeedRunTailFrom;
    }

    /// <summary>Command-to-handler map for the dispatcher (built once per editor).</summary>
    private Dictionary<EditorCommand, Action<ConsoleKeyInfo>> BuildCommandMap() => new()
    {
        [EditorCommand.ToggleMenu] = _ => OpenMenu(0),
        [EditorCommand.OpenMenuFile] = _ => OpenMenu(0),
        [EditorCommand.OpenMenuEdit] = _ => OpenMenu(1),
        [EditorCommand.OpenMenuHelp] = _ => OpenMenu(2),
        [EditorCommand.MoveLeft] = _ => MoveLeft(),
        [EditorCommand.MoveRight] = _ => MoveRight(),
        [EditorCommand.MoveUp] = _ => MoveUp(),
        [EditorCommand.MoveDown] = _ => MoveDown(),
        [EditorCommand.GoHome] = _ => GoHome(),
        [EditorCommand.GoEnd] = _ => GoEnd(),
        [EditorCommand.GoDocStart] = _ => GoDocStart(),
        [EditorCommand.GoDocEnd] = _ => GoDocEnd(),
        [EditorCommand.PageUp] = _ => MovePage(-1),
        [EditorCommand.PageDown] = _ => MovePage(1),
        [EditorCommand.WordLeft] = _ => MoveWordLeft(),
        [EditorCommand.WordRight] = _ => MoveWordRight(),
        [EditorCommand.DelWordBefore] = _ => DeleteWordBefore(),
        [EditorCommand.DelWordAfter] = _ => DeleteWordAfter(),
        [EditorCommand.Save] = _ => Save(),
        [EditorCommand.Quit] = _ => TryQuit(),
        [EditorCommand.NewFile] = _ => NewTab(),
        [EditorCommand.OpenFile] = _ => DoOpen(),
        [EditorCommand.OpenRecent] = _ => DoRecent(),
        [EditorCommand.About] = _ => _dialog = new ModalDialog(ModalState.About(_loc, AppVersion), ApplyModalOutcome),
        [EditorCommand.Help] = _ => RunDialog(new HelpDialog(_loc)),
        [EditorCommand.ToggleLineNumbers] = _ =>
        {
            _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
            _store.Save(_settings);
            SetMessage($"{_loc["settings.shownumbers"]}: {OnOff(_settings.ShowLineNumbers)}");
        },
        [EditorCommand.ToggleWrap] = _ =>
        {
            _settings.WordWrap = !_settings.WordWrap;
            _store.Save(_settings);
            SetMessage($"{_loc["settings.wordwrap"]}: {OnOff(_settings.WordWrap)}");
        },
        [EditorCommand.ToggleWhitespace] = _ =>
        {
            _settings.ShowWhitespace = !_settings.ShowWhitespace;
            _store.Save(_settings);
            SetMessage($"{_loc["settings.whitespace"]}: {OnOff(_settings.ShowWhitespace)}");
        },
        [EditorCommand.ToggleSidebar] = _ => ToggleSidebar(),
        [EditorCommand.NewTab] = _ => NewTab(),
        [EditorCommand.CloseTab] = _ => CloseTab(),
        [EditorCommand.NextTab] = _ => SwitchTab(_active + 1),
        [EditorCommand.PrevTab] = _ => SwitchTab(_active - 1),
        [EditorCommand.GoTabNumber] = k =>
        {
            int tabN = k.Key switch
            {
                >= ConsoleKey.D1 and <= ConsoleKey.D9 => (int)k.Key - (int)ConsoleKey.D0,
                ConsoleKey.D0 => 10,
                _ => -1,
            };
            if (tabN >= 1 && tabN <= _docs.Count)
                SwitchTab(tabN - 1);
        },
        [EditorCommand.ListTabs] = _ => ListTabs(),
        [EditorCommand.SplitPane] = _ => SplitPane(),
        [EditorCommand.NextPane] = _ => SwitchPane(_pane + 1),
        [EditorCommand.PrevPane] = _ => SwitchPane(_pane - 1),
        [EditorCommand.GoPaneNumber] = k =>
        {
            int paneN = k.Key is >= ConsoleKey.D1 and <= ConsoleKey.D9
                ? (int)k.Key - (int)ConsoleKey.D0 : -1;
            if (paneN >= 1 && paneN <= _panes.Count)
                SwitchPane(paneN - 1);
        },
        [EditorCommand.Settings] = _ => RunSettings(),
        [EditorCommand.CommandPalette] = k =>
        {
            EditorCommand? picked = null;
            RunDialog(new CommandPaletteDialog(_settings, _store, ApplySettings, cmd => picked = cmd));
            if (picked is { } pc)
                _dispatcher.Execute(pc, k); // Runs the command after the palette closes (no nesting)
        },
        [EditorCommand.QuickOpen] = _ => QuickOpenFlow(),
        [EditorCommand.CommandLine] = _ => CommandLineFlow(),
        [EditorCommand.Find] = _ => Find(),
        [EditorCommand.Grep] = _ => GrepFlow(),
        [EditorCommand.FindNext] = _ => FindNext(),
        [EditorCommand.FindPrev] = _ => FindPrev(),
        [EditorCommand.GoToDefinition] = _ => GoToDefinitionFlow(),
        [EditorCommand.Replace] = _ => Replace(),
        [EditorCommand.GoToLine] = _ => GoToLine(),
        [EditorCommand.DocStats] = _ =>
        {
            var st = _buf.CountStats();
            SetMessage(_loc.Format("msg.stats", st.Lines, st.Words, st.Chars));
        },
        [EditorCommand.CutLine] = _ => CutLine(),
        [EditorCommand.CopyLine] = _ => CopyLine(),
        [EditorCommand.Paste] = _ => Paste(),
        [EditorCommand.RegisterPick] = _ => RegisterPickFlow(),
        [EditorCommand.DuplicateLine] = _ => DuplicateBlock(),
        [EditorCommand.ToggleComment] = _ => ToggleComment(),
        [EditorCommand.ToggleBookmark] = _ => ToggleBookmark(),
        [EditorCommand.NextBookmark] = _ => NextBookmark(),
        [EditorCommand.ToggleFold] = _ => ToggleFold(),
        [EditorCommand.CompleteWord] = _ => CompleteWord(),
        [EditorCommand.SortLines] = _ => SortBlock(),
        [EditorCommand.ShellFilter] = _ => ShellFilterFlow(),
        [EditorCommand.SurroundAdd] = _ => SurroundAddFlow(),
        [EditorCommand.SurroundChange] = _ => SurroundChangeFlow(),
        [EditorCommand.SurroundDelete] = _ => SurroundDeleteFlow(),
        [EditorCommand.JumpBack] = _ => JumpBack(),
        [EditorCommand.JumpForward] = _ => JumpForward(),
        [EditorCommand.GoBracketMatch] = _ => JumpToBracket(),
        [EditorCommand.MoveLineUp] = _ => MoveLineBlock(-1),
        [EditorCommand.MoveLineDown] = _ => MoveLineBlock(1),
        [EditorCommand.CaretAddAbove] = _ => CaretAddAbove(),
        [EditorCommand.CaretAddBelow] = _ => CaretAddBelow(),
        [EditorCommand.CaretAddNext] = _ => CaretAddNext(),
        [EditorCommand.CaretClear] = _ => ClearExtraCarets(),
        [EditorCommand.SaveAs] = _ => SaveAs(),
        [EditorCommand.SaveAll] = _ => SaveAll(),
        [EditorCommand.FileFormat] = _ => RunDialog(new FormatDialog(_buf)),
        [EditorCommand.TrimTrailing] = _ =>
        {
            int trimmed = _buf.TrimTrailingWhitespace();
            ClampCursor(); TrackCol();
            SetMessage(_loc.Format("msg.trimmed", trimmed));
        },
        [EditorCommand.Undo] = _ => { _buf.Undo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.undo"]); },
        [EditorCommand.Redo] = _ => { _buf.Redo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.redo"]); },
        [EditorCommand.InsertEnter] = _ =>
        {
            if (HasExtraCarets) { MultiEnter(); return; }
            DeleteSelection(); // Replaces the selection
            (_row, _col) = _buf.SplitLine(_row, _col);
            _docs[_active].ShiftMarks(_row, 1);
            TrackCol();
        },
        [EditorCommand.InsertBackspace] = _ =>
        {
            if (HasExtraCarets) { MultiBackspace(); return; }
            if (DeleteSelection()) return; // Erases the selection instead of a character
            if (_settings.AutoPairs && _buf.DeletePair(_row, _col) is (int ar, int ac))
            {
                (_row, _col) = (ar, ac);
                TrackCol();
                return;
            }
            int br = _row, bc = _col;
            (_row, _col) = _buf.Backspace(_row, _col);
            if (br > 0 && bc == 0)
                _docs[_active].ShiftMarks(br, -1);
            TrackCol();
        },
        [EditorCommand.InsertDelete] = _ =>
        {
            if (HasExtraCarets) { MultiDelete(); return; }
            if (DeleteSelection()) return; // Erases the selection instead of a character
            int dr = _row, dc = _col, dl = _buf.GetLine(dr).Length, dn = _buf.Count;
            (_row, _col) = _buf.Delete(_row, _col);
            if (dc >= dl && dr + 1 < dn)
                _docs[_active].ShiftMarks(dr + 1, -1);
            TrackCol();
        },
        [EditorCommand.InsertTab] = _ =>
        {
            if (HasExtraCarets) { MultiTab(); return; }
            if (_sel.HasSelection(_row, _col)) { IndentSelection(); return; }
            _buf.InsertString(_row, _col, _buf.IndentString);
            _col += _buf.IndentString.Length;
            TrackCol();
        },
        [EditorCommand.Unindent] = _ => UnindentSelectionOrLine(),
        [EditorCommand.SelectAll] = _ => SelectAll(),
        [EditorCommand.InsertChar] = k =>
        {
            if (HasExtraCarets) { MultiInsertChar(k.KeyChar); _speedCount = 0; return; }
            if (DeleteSelection()) // Replaces the selection with input
                _speedCount = 0;
            bool speedTail = TrackSpeedRun(k);
            if (_settings.AutoPairs && !speedTail && TryAutoPair(k.KeyChar))
            {
                _speedCount = 0;
                return;
            }
            _buf.InsertChar(_row, _col, k.KeyChar);
            _col++;
            if (speedTail)
                _buf.CoalesceUndo(_speedCount);
            TrackCol();
        },
        // EditorCommand.None — no entry: Esc outside dialogs, Alt, unknown Ctrl combos do nothing.
    };

    private void Execute(EditorCommand cmd, ConsoleKeyInfo k)
    {
        if (cmd != EditorCommand.InsertChar)
            _speedCount = 0; // any other command ends a wire-speed run
        if (cmd is not (EditorCommand.CutLine or EditorCommand.CopyLine
                or EditorCommand.Paste or EditorCommand.RegisterPick))
            _pendingRegister = null; // a picked register is one-shot for the next yank/paste
        _dispatcher.Execute(cmd, k);
    }

    /// <summary>Routes a key while a menu is open.</summary>
    private void HandleMenuKey(ConsoleKeyInfo k)
    {
        if (k.Key is ConsoleKey.Escape or ConsoleKey.F10)
        {
            _menu = null;
            return;
        }
        if ((k.Modifiers & ConsoleModifiers.Alt) != 0
            && (k.Modifiers & ConsoleModifiers.Control) == 0
            && k.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            int mi = _menu!.FindMenuByHotkey((char)k.Key);
            if (mi >= 0)
                _menu.Open(mi);
            return;
        }
        switch (k.Key)
        {
            case ConsoleKey.UpArrow: _menu!.MoveUp(); return;
            case ConsoleKey.DownArrow: _menu!.MoveDown(); return;
            case ConsoleKey.LeftArrow: _menu!.MoveLeft(); return;
            case ConsoleKey.RightArrow: _menu!.MoveRight(); return;
            case ConsoleKey.Home:
                _menu!.Open(_menu.OpenIndex);
                return;
            case ConsoleKey.Enter:
                if (!_menu!.Selected.IsSeparator)
                    ActivateMenuItem(_menu.Selected);
                return;
        }
        if ((k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && k.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            MenuItem? item = _menu!.FindItemByHotkey((char)k.Key);
            if (item is not null)
                ActivateMenuItem(item);
            return;
        }
        _menu = null;
        Execute(_keys.Map(k), k);
    }

    private void ActivateMenuItem(MenuItem item)
    {
        _menu = null;
        Execute(item.Command, new ConsoleKeyInfo('\0', ConsoleKey.NoName, false, false, false));
    }

    private void OpenMenu(int index)
    {
        _menu = new MenuState(BuildMenus(_loc));
        _menu.Open(index);
    }

    /// <summary>Builds the menu bar: File / Edit / Help.</summary>
    /// <summary>Gets a menu hint: a binding override or the default literal.</summary>
    private static string? Hint(string? literal, EditorCommand cmd) =>
        KeyMap.HintFor(cmd) ?? literal;

    internal static List<TopMenu> BuildMenus(Loc loc) => new()
    {
        new TopMenu(loc["menu.file"], 'F', new List<MenuItem>
        {
            new(loc["menu.newtab"], 'T', Hint("Ctrl+T", EditorCommand.NewTab), EditorCommand.NewTab),
            new(loc["menu.open"], 'O', Hint(null, EditorCommand.OpenFile), EditorCommand.OpenFile),
            new(loc["menu.recent"], 'R', Hint(null, EditorCommand.OpenRecent), EditorCommand.OpenRecent),
            new(loc["menu.save"], 'S', Hint("^S", EditorCommand.Save), EditorCommand.Save),
            new(loc["menu.saveas"], 'A', Hint("Ctrl+Shift+S", EditorCommand.SaveAs), EditorCommand.SaveAs),
            new(loc["menu.saveall"], 'L', Hint(null, EditorCommand.SaveAll), EditorCommand.SaveAll),
            MenuItem.Separator,
            new(loc["menu.format"], 'F', Hint("F9", EditorCommand.FileFormat), EditorCommand.FileFormat),
            new(loc["menu.closetab"], 'W', Hint("Ctrl+W", EditorCommand.CloseTab), EditorCommand.CloseTab),
            MenuItem.Separator,
            new(loc["menu.settings"], 'P', Hint(null, EditorCommand.Settings), EditorCommand.Settings),
            new(loc["menu.exit"], 'X', Hint("^Q", EditorCommand.Quit), EditorCommand.Quit),
        }),
        new TopMenu(loc["menu.edit"], 'E', new List<MenuItem>
        {
            new(loc["menu.undo"], 'U', Hint("^Z", EditorCommand.Undo), EditorCommand.Undo),
            new(loc["menu.redo"], 'R', Hint("^Y", EditorCommand.Redo), EditorCommand.Redo),
            MenuItem.Separator,
            new(loc["menu.cut"], 'T', Hint("^K", EditorCommand.CutLine), EditorCommand.CutLine),
            new(loc["menu.copy"], 'C', Hint("^C", EditorCommand.CopyLine), EditorCommand.CopyLine),
            new(loc["menu.paste"], 'P', Hint("^U", EditorCommand.Paste), EditorCommand.Paste),
            new(loc["menu.duplicate"], 'D', Hint("^D", EditorCommand.DuplicateLine), EditorCommand.DuplicateLine),
            new(loc["menu.togglecomment"], 'O', Hint("Ctrl+/", EditorCommand.ToggleComment), EditorCommand.ToggleComment),
            new(loc["menu.sortlines"], 'S', Hint(null, EditorCommand.SortLines), EditorCommand.SortLines),
            MenuItem.Separator,
            new(loc["menu.gobracket"], 'J', Hint("Alt+]", EditorCommand.GoBracketMatch), EditorCommand.GoBracketMatch),
            new(loc["menu.find"], 'F', Hint("^F", EditorCommand.Find), EditorCommand.Find),
            new(loc["menu.replace"], 'H', Hint("^H", EditorCommand.Replace), EditorCommand.Replace),
            new(loc["menu.goto"], 'G', Hint("^G", EditorCommand.GoToLine), EditorCommand.GoToLine),
            new(loc["menu.grep"], 'E', Hint("Ctrl+Shift+F", EditorCommand.Grep), EditorCommand.Grep),
            MenuItem.Separator,
            new(loc["menu.trimtrail"], 'M', Hint(null, EditorCommand.TrimTrailing), EditorCommand.TrimTrailing),
            new(loc["menu.selectall"], 'A', Hint("^A", EditorCommand.SelectAll), EditorCommand.SelectAll),
            new(loc["menu.shellfilter"], 'I', Hint("^R", EditorCommand.ShellFilter), EditorCommand.ShellFilter),
            new(loc["menu.surround.add"], 'V', Hint("^L", EditorCommand.SurroundAdd), EditorCommand.SurroundAdd),
            new(loc["menu.surround.change"], 'N', Hint("Alt+L", EditorCommand.SurroundChange), EditorCommand.SurroundChange),
            new(loc["menu.surround.delete"], 'L', Hint("Alt+J", EditorCommand.SurroundDelete), EditorCommand.SurroundDelete),
        }),
        new TopMenu(loc["menu.help"], 'H', new List<MenuItem>
        {
            new(loc["menu.helpitem"], 'H', Hint("F1", EditorCommand.Help), EditorCommand.Help),
            new(loc["menu.about"], 'A', Hint(null, EditorCommand.About), EditorCommand.About),
        }),
    };

    /// <summary>Runs the shared dialog driver: Render/Read until closed.</summary>
    private void RunDialog(Dialog dlg)
    {
        Dialog? prev = _dialog;
        _dialog = dlg;
        try
        {
            while (!dlg.Closed)
            {
                Render();
                InputEvent ev;
                try
                {
                    ev = InputReader.Read();
                }
                catch (InvalidOperationException)
                {
                    dlg.Cancel();
                    break;
                }
                if (ev is PasteInput paste)
                {
                    dlg.Paste(paste.Text);
                    continue;
                }
                if (ev is KeyInput key)
                    dlg.HandleKey(key.Key);
                else if (ev is MouseInput mm && mm.Action == MouseAction.LeftPress)
                {
                    int mw, mh;
                    try
                    {
                        mw = Console.WindowWidth;
                        mh = Console.WindowHeight;
                    }
                    catch
                    {
                        continue;
                    }
                    dlg.HandleClick(mm.X, mm.Y, mw, mh, _loc);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_dialog, dlg))
                _dialog = prev;
        }
    }

    /// <summary>Runs the file manager as a modal window (path or null on Esc).</summary>
    private string? RunPicker(PickerMode mode, string startDir, string initialName)
    {
        var dlg = new FileDialog(new FilePickerState(mode, startDir, initialName),
            _loc["picker.open.title"], _loc["picker.save.title"]);
        RunDialog(dlg);
        return dlg.Result;
    }

    private string? PickSavePath(string initialName) =>
        RunPicker(PickerMode.Save, StartDir(), initialName);

    private string? Prompt(
        string title, string initial, string kind = "",
        bool liveHighlight = false, bool showOptions = false)
    {
        var field = new LineField();
        field.Set(initial ?? string.Empty);
        field.End(select: false);
        string draft = field.Text;
        int histIdx = -1;
        IReadOnlyList<string> Hist() =>
            kind.Length == 0 ? [] : _history.Get(kind);
        void Recall(int idx)
        {
            IReadOnlyList<string> h = Hist();
            field.Set(idx < 0 ? draft : h[idx]);
            field.End(select: false);
        }
        // Live highlight: preview term plus rerender on every keystroke (leaves the cursor).
        void RefreshLive()
        {
            if (!liveHighlight)
                return;
            _liveSearch = field.Text;
            Render();
        }
        RefreshLive();
        while (true)
        {
            DrawPrompt(title, field.Text, field.Pos, showOptions);
            InputEvent ev;
            try
            {
                ev = InputReader.Read();
            }
            catch (InvalidOperationException)
            {
                _liveSearch = null;
                return null;
            }
            if (ev is PasteInput paste)
            {
                field.Insert(paste.Text.Replace("\r", "").Replace("\n", ""));
                RefreshLive();
                continue;
            }
            if (ev is MouseInput mm)
            {
                // Click on [x] toggles (option row mirrors DrawPrompt).
                if (showOptions && _screen.Height >= 5
                    && mm.Action == MouseAction.LeftPress && mm.Y == _screen.Height - 2
                    && PromptOptionHit(mm.X, _loc["settings.matchcase"],
                        _loc["settings.wholeword"], _loc["settings.useregex"]) is char t)
                {
                    ConsoleKey key = t switch { 'C' => ConsoleKey.C, 'W' => ConsoleKey.W, _ => ConsoleKey.R };
                    if (ToggleSearchOption(key))
                        RefreshLive();
                }
                continue;
            }
            if (ev is not KeyInput ki)
                continue;
            ConsoleKeyInfo k = ki.Key;
            bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
            bool alt = (k.Modifiers & ConsoleModifiers.Alt) != 0;
            bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (showOptions && alt && !ctrl && ToggleSearchOption(k.Key))
            {
                RefreshLive();
                continue;
            }
            if (ctrl)
            {
                switch (k.Key)
                {
                    case ConsoleKey.LeftArrow: field.MoveWord(-1, shift); break;
                    case ConsoleKey.RightArrow: field.MoveWord(1, shift); break;
                    case ConsoleKey.Backspace: field.DeleteWord(-1); RefreshLive(); break;
                    case ConsoleKey.Delete: field.DeleteWord(1); RefreshLive(); break;
                }
                continue;
            }
            switch (k.Key)
            {
                case ConsoleKey.Escape: _liveSearch = null; if (liveHighlight) Render(); return null;
                case ConsoleKey.Enter:
                    _liveSearch = null;
                    if (kind.Length > 0 && field.Text.Length > 0)
                        _history.Push(kind, field.Text);
                    return field.Text;
                case ConsoleKey.Backspace: field.Backspace(); RefreshLive(); break;
                case ConsoleKey.Delete: field.DeleteChar(); RefreshLive(); break;
                case ConsoleKey.LeftArrow: field.Move(-1, shift); break;
                case ConsoleKey.RightArrow: field.Move(1, shift); break;
                case ConsoleKey.Home: field.Home(shift); break;
                case ConsoleKey.End: field.End(shift); break;
                case ConsoleKey.UpArrow:
                    if (Hist().Count > 0 && histIdx + 1 < Hist().Count)
                    {
                        histIdx++;
                        Recall(histIdx);
                        RefreshLive();
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (histIdx > 0)
                    {
                        histIdx--;
                        Recall(histIdx);
                        RefreshLive();
                    }
                    else if (histIdx == 0)
                    {
                        histIdx = -1;
                        Recall(histIdx);
                        RefreshLive();
                    }
                    break;
                case ConsoleKey.Tab:
                    if (kind == "cmdline")
                    {
                        (string t, int p) = PromptComplete.CompleteCmdline(field.Text, field.Pos);
                        field.Set(t);
                        field.MoveTo(p);
                        RefreshLive();
                    }
                    else if (kind is "find" or "replace-find" or "grep-pattern")
                    {
                        int start = field.Pos;
                        while (start > 0 && TextBuffer.IsWordChar(field.Text[start - 1]))
                            start--;
                        string prefix = field.Text[start..field.Pos];
                        if (prefix.Length > 0)
                        {
                            (string t, int p) = PromptComplete.CompleteWord(
                                field.Text, field.Pos, Completion.Collect(_buf, prefix));
                            field.Set(t);
                            field.MoveTo(p);
                            RefreshLive();
                        }
                    }
                    break;
                default:
                    if (!char.IsControl(k.KeyChar))
                    {
                        field.Insert(k.KeyChar.ToString());
                        RefreshLive();
                    }
                    break;
            }
        }
    }

    private static char Check(bool v) => v ? 'x' : ' ';

    /// <summary>Hits the toggle under a coordinate in the prompt option row ('C'/'W'/'R' or null). Pure.</summary>
    internal static char? PromptOptionHit(int x, string matchCase, string wholeWord, string useRegex)
    {
        // Mirrors DrawPrompt: "[x] label (Alt+C)  [ ] label (Alt+W)  [ ] label (Alt+R".
        string[] segs = [
            $"[ ] {matchCase} (Alt+C)",
            $"[ ] {wholeWord} (Alt+W)",
            $"[ ] {useRegex} (Alt+R)",
        ];
        int cx = 0;
        char[] keys = ['C', 'W', 'R'];
        for (int i = 0; i < segs.Length; i++)
        {
            if (x >= cx && x < cx + segs[i].Length)
                return keys[i];
            cx += segs[i].Length + 2; // Two-space separator
        }
        return null;
    }

    private void DrawPrompt(string title, string input, int pos, bool showOptions = false)
    {
        try
        {
            if (Console.WindowWidth != _screen.Width || Console.WindowHeight != _screen.Height)
                Render();
        }
        catch { }
        int w = _screen.Width, h = _screen.Height;
        if (w < 10 || h < 4)
            return;
        if (showOptions && h >= 5)
        {
            string opts = "[" + Check(_settings.SearchMatchCase) + "] " + _loc["settings.matchcase"] + " (Alt+C)  "
                + "[" + Check(_settings.SearchWholeWord) + "] " + _loc["settings.wholeword"] + " (Alt+W)  "
                + "[" + Check(_settings.SearchUseRegex) + "] " + _loc["settings.useregex"] + " (Alt+R)";
            if (opts.Length > w)
                opts = opts[..w];
            _screen.Text(0, h - 2, opts.PadRight(w)[..w], _theme.PromptFg, _theme.PromptBg);
        }
        int row = h - 1;
        string text = title + input;
        if (text.Length > w)
            text = text[^w..];
        _screen.Text(0, row, text.PadRight(w)[..w], _theme.PromptFg, _theme.PromptBg);
        Terminal.BeginSynchronizedUpdate();
        try
        {
            _screen.Flush();
        }
        finally
        {
            Terminal.EndSynchronizedUpdate();
        }
        int cursorX = Math.Min(w - 1, title.Length + pos - Math.Max(0, (title.Length + input.Length) - w));
        try
        {
            Console.SetCursorPosition(Math.Max(0, cursorX), row);
            Console.CursorVisible = true;
        }
        catch { }
    }
}
