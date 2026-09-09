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
                _dialog.Paste(paste.Text); // менеджер вставляет, остальные игнорят
                return;
            }
            _menu = null;
            DeleteSelection();
            ClampCursor();
            int pr = _row;
            int pbefore = _buf.Count;
            (_row, _col) = _buf.InsertText(_row, _col, paste.Text);
            if (_buf.Count > pbefore)
            {
                _docs[_active].ShiftBookmarks(pr + 1, _buf.Count - pbefore);
                _docs[_active].ShiftFolds(pr + 1, _buf.Count - pbefore);
            }
            ClampCursor();
            TrackCol();
            return;
        }
        if (input is MouseInput mouse)
        {
            HandleMouse(mouse);
            return;
        }
        _mouseActive = false; // клавиатура/вставка — hover гаснет
        _mouseDrag = false; // клавиша mid-drag — тяга стоп, выделение как есть
        if (input is KeyInput key)
            HandleKey(key.Key);
        // Будущие типы событий — игнор, а не каст (мышь уже роняла это место).
    }

    /// <summary>Мышь: размеры — из консоли, дальше — чистая логика.</summary>
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
        HandleMouseAt(m, w, h);
    }

    /// <summary>
    /// Мышь: движение — только hover; модалка — кнопки/колесо/фон-закрыть;
    /// меню — по кнопкам; табы/панели — фокус; колесо мимо — скролл активной панели.
    /// Приоритет: модалка → диалог → меню → бар → табы/текст.
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
            // hover — только позиция; тяга с зажатой левой — растянуть,
            // отпускание (m) — завершить. Активации контролов на отпускании
            // нет сознательно: срабатывание — всегда на press (см. ниже).
            if (_mouseDrag && m.Button == MouseButton.Left)
                ExtendMouseDrag(m, w, h);
            else if (_mouseDrag)
                EndMouseDrag(copy: true);
            return;
        }
        _mouseDrag = false; // любое не-движение без viewport-press разоружает тягу
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
                return; // средняя/правая — потребителей нет
            if (!md.HandleClick(m.X, m.Y, w, h, _loc))
            {
                // Мимо бокса — как Esc: отмена без исхода (была фокус-ловушка).
                md.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
            }
            if (ReferenceEquals(_dialog, md) && md.Closed)
                _dialog = null; // как клавиатурный путь: исход без нового диалога — убрать
            return;
        }
        if (_dialog is not null)
            return; // RunDialog-диалоги (настройки, менеджер): мыши пока нет
        if (m.Action == MouseAction.RightPress && _menu is not null)
            return; // правая по меню — игнор, пункты жмут только левой
        if (_menu is not null && HandleMenuMouse(m))
            return;
        // Мимо меню (закрыли) — клик доезжает до текста, как клавиша в HandleMenuKey.
        if (w < 20 || h < 5)
            return;
        if (_menu is null && m.Y == 0 && m.Action == MouseAction.LeftPress)
        {
            // Закрытое меню: клик по бару открывает (в текст y=0 не попадает).
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
            // Колесо вне модалки/меню — всегда скролл активной панели,
            // даже над гуттером, сайдбаром и статусбаром (fallback).
            ScrollWheel(m.Action == MouseAction.WheelDown ? 1 : -1, m.Count);
            return;
        }
        if (m.Action is not (MouseAction.LeftPress or MouseAction.RightPress))
            return; // средняя — потребителей нет
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
            return; // сайдбар: кликов нет
        if (pane != _pane)
            SwitchPane(pane); // клик в чужую панель — сначала фокус
        var g = TextGeom(layout);
        if (m.X < g.cx0 || m.X >= g.x1 || m.Y < g.y0 || m.Y >= g.y1)
            return; // гуттер/разделитель/статусбар: кликов нет
        if (m.Action == MouseAction.RightPress)
        {
            CopyMouseSelection(); // есть выделение — в буфер, нет — игнор
            return;
        }
        (int row, int col) = LocateClick(_buf.Lines, _docs[_active].Folds, _top, _topSeg,
            _settings.WordWrap, g.cw, _left, m.Y - g.y0, m.X - g.cx0);
        _sel.Clear();
        _row = row;
        _col = col;
        ClampCursor();
        TrackCol();
        _mouseDrag = true; // press в тексте: якорь для тяги
        _mousePane = pane;
        _mouseRow = _row;
        _mouseCol = _col;
    }

    /// <summary>Геометрия текста активной панели (гуттер — по её буферу).</summary>
    private (int cx0, int x1, int y0, int y1, int cw) TextGeom(EditorLayout layout)
    {
        int numWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
        int gutterWidth = _settings.ShowLineNumbers ? numWidth + 4 : 0;
        int cw = Math.Max(1, layout.PaneWs[_pane] - gutterWidth);
        int cx0 = layout.PaneXs[_pane] + gutterWidth;
        return (cx0, layout.PaneXs[_pane] + layout.PaneWs[_pane], layout.Y0,
            layout.Y0 + layout.TextHeight, cw);
    }

    /// <summary>Тяга: растянуть выделение от якоря press до позиции мыши.</summary>
    private void ExtendMouseDrag(MouseInput m, int w, int h)
    {
        if (_mousePane < 0 || _mousePane >= _panes.Count || _mousePane != _pane)
        {
            _mouseDrag = false; // фокус mid-drag уехал клавишами — стоп
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
            _sel.Start(_mouseRow, _mouseCol); // первая подвижка — якорим press
        _row = row;
        _col = col;
        ClampCursor();
        TrackCol();
    }

    /// <summary>Отпускание после тяги: есть выделение — политика копии, нет — гасим.</summary>
    private void EndMouseDrag(bool copy)
    {
        _mouseDrag = false;
        if (!_sel.HasSelection(_row, _col))
        {
            _sel.Clear();
            return;
        }
        if (copy && _settings.CopyOnSelect)
            CopyMouseSelection(); // копирует и гасит
        // Иначе выделение остаётся для клавиатурных операций.
    }

    /// <summary>Выделение — во внутренний буфер + OSC 52, с сообщением; пустое — игнор.</summary>
    private void CopyMouseSelection()
    {
        if (!_sel.HasSelection(_row, _col))
            return;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        _clipboard.Clear();
        _clipboard.AddRange(_buf.GetRangeText(sr, sc, er, ec));
        SystemClipboard.TryExport(_clipboard);
        SetMessage(_loc.Format("msg.copy.sel", SelectionLength(sr, sc, er, ec)));
        _sel.Clear();
    }

    /// <summary>
    /// Вкладка под координатой X внутри панели (зеркало DrawTabs).
    /// Возвращает индекс или null (мимо/одна вкладка).
    /// </summary>
    internal static int? TabHit(
        IReadOnlyList<string> titles, int active, int tabLeft, int paneW, int x)
    {
        if (titles.Count <= 1 || x < 0 || x >= paneW)
            return null;
        int[] widths = new int[titles.Count];
        for (int i = 0; i < titles.Count; i++)
            widths[i] = (i > 0 ? 1 : 0) + titles[i].Length + 2;
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
    /// Мышь при открытом меню: бар — открыть/переключить, дропдаун — пункт, мимо — закрыть.
    /// Возвращает false, если меню закрыто кликом мимо (клик доезжает до текста).
    /// </summary>
    private bool HandleMenuMouse(MouseInput m)
    {
        int w, h;
        try
        {
            w = Console.WindowWidth;
            h = Console.WindowHeight;
        }
        catch
        {
            return true;
        }
        if (_menu is null)
            return true;
        if (m.Action != MouseAction.LeftPress)
        {
            _menu = null; // колесо и прочие — мимо, закрываем
            return true;
        }
        List<TopMenu> menus = _menu.Menus;
        if (m.Y == 0)
        {
            int? hit = MenuHit.BarHit(menus, m.X, w);
            int was = _menu.OpenIndex;
            _menu = null;
            if (hit is not null && hit.Value != was)
                OpenMenu(hit.Value); // другое меню — переключить
            // повтор по своему / мимо ячеек — закрыто
            return true;
        }
        int menuX = 0;
        for (int i = 0; i < _menu.OpenIndex && i < menus.Count; i++)
            menuX += menus[i].Label.Length + 2;
        int? item = MenuHit.DropdownHit(_menu.Current, menuX, m.X, m.Y, w, h);
        if (item is null)
        {
            _menu = null;
            return false; // мимо — закрыть и отдать клик тексту
        }
        ActivateMenuItem(_menu.Current.Items[item.Value]);
        return true;
    }

    /// <summary>Колесо: курсор ±3 строки за тик (steps — склейка пачки тиков).</summary>
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
        // Открытый диалог глотает весь ввод (как modal_end в MS Edit).
        if (_dialog is not null)
        {
            Dialog d = _dialog;
            d.HandleKey(k);
            if (ReferenceEquals(_dialog, d) && d.Closed)
                _dialog = null;
            return;
        }

        // Открытое меню перехватывает ввод (см. menubar_menu_end в MS Edit).
        if (_menu is not null)
        {
            HandleMenuKey(k);
            return;
        }

        // Фокус в панели файлов: навигация и Enter глотаются панелью.
        if (_sidebarFocus && _sidebar is not null)
        {
            HandleSidebarKey(k);
            return;
        }

        // Esc гасит активное выделение.
        if (k.Key == ConsoleKey.Escape && _sel.HasSelection(_row, _col))
        {
            _sel.Clear();
            return;
        }

        EditorCommand cmd = KeyMap.Map(k);

        if (IsMovement(cmd))
        {
            bool extend = (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (extend && !_sel.HasSelection(_row, _col))
                _sel.Start(_row, _col);
            Execute(cmd, k);
            if (!extend)
                _sel.Clear();
            return;
        }

        Execute(cmd, k);
    }

    /// <summary>Команды движения курсора (участвуют в Shift-выделении).</summary>
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

    private void Execute(EditorCommand cmd, ConsoleKeyInfo k)
    {
        switch (cmd)
        {
            case EditorCommand.ToggleMenu: OpenMenu(0); return;
            case EditorCommand.OpenMenuFile: OpenMenu(0); return;
            case EditorCommand.OpenMenuEdit: OpenMenu(1); return;
            case EditorCommand.OpenMenuHelp: OpenMenu(2); return;
            case EditorCommand.MoveLeft: MoveLeft(); return;
            case EditorCommand.MoveRight: MoveRight(); return;
            case EditorCommand.MoveUp: MoveUp(); return;
            case EditorCommand.MoveDown: MoveDown(); return;
            case EditorCommand.GoHome: GoHome(); return;
            case EditorCommand.GoEnd: GoEnd(); return;
            case EditorCommand.GoDocStart: GoDocStart(); return;
            case EditorCommand.GoDocEnd: GoDocEnd(); return;
            case EditorCommand.PageUp: MovePage(-1); return;
            case EditorCommand.PageDown: MovePage(1); return;
            case EditorCommand.WordLeft: MoveWordLeft(); return;
            case EditorCommand.WordRight: MoveWordRight(); return;
            case EditorCommand.DelWordBefore: DeleteWordBefore(); return;
            case EditorCommand.DelWordAfter: DeleteWordAfter(); return;
            case EditorCommand.Save: Save(); return;
            case EditorCommand.Quit: TryQuit(); return;
            case EditorCommand.NewFile: NewTab(); return;
            case EditorCommand.OpenFile: DoOpen(); return;
            case EditorCommand.OpenRecent: DoRecent(); return;
            case EditorCommand.About: _dialog = new ModalDialog(ModalState.About(_loc, AppVersion), ApplyModalOutcome); return;
            case EditorCommand.Help: RunDialog(new HelpDialog(_loc)); return;
            case EditorCommand.ToggleLineNumbers:
                _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
                _store.Save(_settings);
                SetMessage($"{_loc["settings.shownumbers"]}: {OnOff(_settings.ShowLineNumbers)}");
                return;
            case EditorCommand.ToggleWrap:
                _settings.WordWrap = !_settings.WordWrap;
                _store.Save(_settings);
                SetMessage($"{_loc["settings.wordwrap"]}: {OnOff(_settings.WordWrap)}");
                return;
            case EditorCommand.ToggleWhitespace:
                _settings.ShowWhitespace = !_settings.ShowWhitespace;
                _store.Save(_settings);
                SetMessage($"{_loc["settings.whitespace"]}: {OnOff(_settings.ShowWhitespace)}");
                return;
            case EditorCommand.ToggleSidebar: ToggleSidebar(); return;
            case EditorCommand.NewTab: NewTab(); return;
            case EditorCommand.CloseTab: CloseTab(); return;
            case EditorCommand.NextTab: SwitchTab(_active + 1); return;
            case EditorCommand.PrevTab: SwitchTab(_active - 1); return;
            case EditorCommand.GoTabNumber:
                int tabN = k.Key switch
                {
                    >= ConsoleKey.D1 and <= ConsoleKey.D9 => (int)k.Key - (int)ConsoleKey.D0,
                    ConsoleKey.D0 => 10,
                    _ => -1,
                };
                if (tabN >= 1 && tabN <= _docs.Count)
                    SwitchTab(tabN - 1);
                return;
            case EditorCommand.ListTabs: ListTabs(); return;
            case EditorCommand.SplitPane: SplitPane(); return;
            case EditorCommand.NextPane: SwitchPane(_pane + 1); return;
            case EditorCommand.PrevPane: SwitchPane(_pane - 1); return;
            case EditorCommand.GoPaneNumber:
                int paneN = k.Key is >= ConsoleKey.D1 and <= ConsoleKey.D9
                    ? (int)k.Key - (int)ConsoleKey.D0 : -1;
                if (paneN >= 1 && paneN <= _panes.Count)
                    SwitchPane(paneN - 1);
                return;
            case EditorCommand.Settings: RunSettings(); return;
            case EditorCommand.Find: Find(); return;
            case EditorCommand.Grep: GrepFlow(); return;
            case EditorCommand.FindNext: FindNext(); return;
            case EditorCommand.FindPrev: FindPrev(); return;
            case EditorCommand.Replace: Replace(); return;
            case EditorCommand.GoToLine: GoToLine(); return;
            case EditorCommand.DocStats:
                var st = _buf.CountStats();
                SetMessage(_loc.Format("msg.stats", st.Lines, st.Words, st.Chars));
                return;
            case EditorCommand.CutLine: CutLine(); return;
            case EditorCommand.CopyLine: CopyLine(); return;
            case EditorCommand.Paste: Paste(); return;
            case EditorCommand.DuplicateLine: DuplicateBlock(); return;
            case EditorCommand.ToggleComment: ToggleComment(); return;
            case EditorCommand.ToggleBookmark: ToggleBookmark(); return;
            case EditorCommand.NextBookmark: NextBookmark(); return;
            case EditorCommand.ToggleFold: ToggleFold(); return;
            case EditorCommand.CompleteWord: CompleteWord(); return;
            case EditorCommand.SortLines: SortBlock(); return;
            case EditorCommand.GoBracketMatch: JumpToBracket(); return;
            case EditorCommand.MoveLineUp: MoveLineBlock(-1); return;
            case EditorCommand.MoveLineDown: MoveLineBlock(1); return;
            case EditorCommand.SaveAs: SaveAs(); return;
            case EditorCommand.SaveAll: SaveAll(); return;
            case EditorCommand.FileFormat: RunDialog(new FormatDialog(_buf)); return;
            case EditorCommand.TrimTrailing:
                int trimmed = _buf.TrimTrailingWhitespace();
                ClampCursor(); TrackCol();
                SetMessage(_loc.Format("msg.trimmed", trimmed));
                return;
            case EditorCommand.Undo: _buf.Undo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.undo"]); return;
            case EditorCommand.Redo: _buf.Redo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.redo"]); return;
            case EditorCommand.InsertEnter:
                DeleteSelection(); // замена выделения
                (_row, _col) = _buf.SplitLine(_row, _col);
                _docs[_active].ShiftBookmarks(_row, 1);
                _docs[_active].ShiftFolds(_row, 1);
                TrackCol();
                return;
            case EditorCommand.InsertBackspace:
                if (DeleteSelection()) return; // стереть выделение вместо символа
                {
                    int br = _row, bc = _col;
                    (_row, _col) = _buf.Backspace(_row, _col);
                    if (br > 0 && bc == 0)
                    {
                        _docs[_active].ShiftBookmarks(br, -1);
                        _docs[_active].ShiftFolds(br, -1);
                    }
                }
                TrackCol();
                return;
            case EditorCommand.InsertDelete:
                if (DeleteSelection()) return; // стереть выделение вместо символа
                {
                    int dr = _row, dc = _col, dl = _buf.GetLine(dr).Length, dn = _buf.Count;
                    (_row, _col) = _buf.Delete(_row, _col);
                    if (dc >= dl && dr + 1 < dn)
                    {
                        _docs[_active].ShiftBookmarks(dr + 1, -1);
                        _docs[_active].ShiftFolds(dr + 1, -1);
                    }
                }
                TrackCol();
                return;
            case EditorCommand.InsertTab:
                if (_sel.HasSelection(_row, _col)) { IndentSelection(); return; }
                _buf.InsertString(_row, _col, _buf.IndentString);
                _col += _buf.IndentString.Length;
                TrackCol();
                return;
            case EditorCommand.Unindent: UnindentSelectionOrLine(); return;
            case EditorCommand.SelectAll: SelectAll(); return;
            case EditorCommand.InsertChar:
                DeleteSelection(); // замена выделения вводом
                _buf.InsertChar(_row, _col, k.KeyChar);
                _col++;
                TrackCol();
                return;
            case EditorCommand.None:
            default: return; // Esc вне диалога, Alt, неизвестные Ctrl-комбинации — ничего
        }
    }

    /// <summary>Маршрут клавиши при открытом меню.</summary>
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
        Execute(KeyMap.Map(k), k);
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

    /// <summary>Меню-бар: File / Edit / Help (состав — как draw_menubar.rs в MS Edit).</summary>
    /// <summary>Подсказка меню: оверрайд биндинга или дефолтный литерал.</summary>
    private static string? Hint(string? literal, EditorCommand cmd) =>
        KeyMap.HintFor(cmd) ?? literal;

    private static List<TopMenu> BuildMenus(Loc loc) => new()
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
        }),
        new TopMenu(loc["menu.help"], 'H', new List<MenuItem>
        {
            new(loc["menu.helpitem"], 'H', Hint("F1", EditorCommand.Help), EditorCommand.Help),
            new(loc["menu.about"], 'A', Hint(null, EditorCommand.About), EditorCommand.About),
        }),
    };

    /// <summary>Общий драйвер диалогов: Render/Read, пока не закроется.</summary>
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

    /// <summary>Файловый менеджер модальным окном (путь или null по Esc).</summary>
    private string? RunPicker(PickerMode mode, string startDir, string initialName)
    {
        var dlg = new FileDialog(new FilePickerState(mode, startDir, initialName),
            _loc["picker.open.title"], _loc["picker.save.title"]);
        RunDialog(dlg);
        return dlg.Result;
    }

    private string? PickSavePath(string initialName) =>
        RunPicker(PickerMode.Save, StartDir(), initialName);

    private string? Prompt(string title, string initial, bool liveHighlight = false, bool showOptions = false)
    {
        var field = new LineField();
        field.Set(initial ?? string.Empty);
        field.End(select: false);
        // Живая подсветка: превью-термин + ререндер на каждое нажатие (курсор не двигаем).
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
                // Клик по тоглам [x] (строка опций — зеркало DrawPrompt).
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
                case ConsoleKey.Enter: _liveSearch = null; return field.Text;
                case ConsoleKey.Backspace: field.Backspace(); RefreshLive(); break;
                case ConsoleKey.Delete: field.DeleteChar(); RefreshLive(); break;
                case ConsoleKey.LeftArrow: field.Move(-1, shift); break;
                case ConsoleKey.RightArrow: field.Move(1, shift); break;
                case ConsoleKey.Home: field.Home(shift); break;
                case ConsoleKey.End: field.End(shift); break;
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

    /// <summary>Тогл под координатой в строке опций промпта ('C'/'W'/'R' или null). Чистая.</summary>
    internal static char? PromptOptionHit(int x, string matchCase, string wholeWord, string useRegex)
    {
        // Зеркало DrawPrompt: "[x] label (Alt+C)  [ ] label (Alt+W)  [ ] label (Alt+R".
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
            cx += segs[i].Length + 2; // два пробела-разделителя
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
        _screen.Flush();
        int cursorX = Math.Min(w - 1, title.Length + pos - Math.Max(0, (title.Length + input.Length) - w));
        try
        {
            Console.SetCursorPosition(Math.Max(0, cursorX), row);
            Console.CursorVisible = true;
        }
        catch { }
    }
}
