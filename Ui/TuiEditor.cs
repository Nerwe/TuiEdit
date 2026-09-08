using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TuiEdit;

internal sealed partial class TuiEditor
{
    private readonly List<Pane> _panes = new(); // сплит-панели, _panes[_pane] — активная
    private int _pane;
    /// <summary>Вкладки активной панели.</summary>
    private List<DocTab> _docs => _panes[_pane].Docs;
    /// <summary>Активная вкладка активной панели.</summary>
    private int _active { get => _panes[_pane].Active; set => _panes[_pane].Active = value; }
    /// <summary>Скролл строки вкладок активной панели.</summary>
    private int _tabLeft { get => _panes[_pane].TabLeft; set => _panes[_pane].TabLeft = value; }
    /// <summary>Буфер активной вкладки.</summary>
    private TextBuffer _buf => _docs[_active].Buf;
    private int _row;
    private int _col;
    private int _desiredCol; // память колонки для Up/Down
    private int _top;        // первая видимая строка
    private int _left;       // первая видимая колонка
    private int _topSeg;     // сегмент первой строки при wrap (иначе 0)
    private string _message = string.Empty;
    private DateTime _messageUntil = DateTime.MinValue;
    private readonly List<string> _clipboard = new();
    private string _lastSearch = string.Empty;
    private string _lastReplace = string.Empty;
    /// <summary>Живой термин подсветки во время ввода в промпте (null — выкл).</summary>
    internal string? _liveSearch;
    private bool _quitRequested;
    private readonly TextSelection _sel = new();
    private MenuState? _menu;   // null — меню-бар закрыт
    private SidebarState? _sidebar; // null — панель файлов закрыта (ширина SidebarState.Width)
    private bool _sidebarFocus;     // ввод идёт в панель, а не в текст
    private Dialog? _dialog;    // null — диалогового окна нет (модалка/менеджер/настройки/справка)
    private PendingOp _pending = PendingOp.None;
    private string _pendingPath = string.Empty;
    private string _overwritePath = string.Empty; // путь из модалки перезаписи
    private string _pendingReplaceTerm = string.Empty; // замена из confirm-модалки
    private string _pendingReplaceRep = string.Empty;
    private string _pendingCompletePrefix = string.Empty; // префикс из попапа дополнения
    private readonly List<GrepHit> _grepHits = new();
    private int _pendingGrepRow;
    private readonly List<string> _recentPaths = new(); // пути из модалки недавних
    private readonly DraftStore _drafts = new(DraftStore.DefaultDir());
    private readonly BackupStore _backups;
    private DateTime _lastDraftAt = DateTime.MinValue;
    private readonly List<(string key, DocDraft draft)> _restoreDrafts = new();
    private readonly List<int> _menuX = new(); // x-координаты меню в баре (из рендера)
    private readonly Screen _screen = new(); // кадр + diff-вывод (без мигания)
    // Ввод читается через статический InputReader.Read (состояния нет).
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private Loc _loc;
    private Theme _theme;

    /// <summary>Версия из сборки (csproj Version); fallback — на случай ручной сборки.</summary>
    internal static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";

    /// <summary>Отложенное действие после диалога «несохранённые изменения».</summary>
    private enum PendingOp { None, Quit, Open, CloseTab }

    public TuiEditor(TextBuffer buf, AppSettings settings, SettingsStore store)
    {
        _panes.Add(new Pane(new DocTab(buf)));
        _settings = settings;
        _store = store;
        _backups = new BackupStore(BackupStore.DefaultDir(store.Path));
        _loc = Loc.Load(settings.Language);
        _theme = ThemeCatalog.Resolve(settings, settings.Theme);
    }

    private BackupStore? Backups => _settings.BackupOnSave ? _backups : null;

    /// <summary>Число вкладок активной панели.</summary>
    internal int TabCount => _docs.Count;

    /// <summary>Индекс активной вкладки активной панели.</summary>
    internal int ActiveTab => _active;

    /// <summary>Число панелей.</summary>
    internal int PaneCount => _panes.Count;

    /// <summary>Индекс активной панели.</summary>
    internal int ActivePane => _pane;

    /// <summary>Сохранить вид активной вкладки в модель.</summary>
    internal void SaveTabState()
    {
        DocTab t = _docs[_active];
        t.Row = _row; t.Col = _col; t.DesiredCol = _desiredCol;
        t.Top = _top; t.Left = _left; t.TopSeg = _topSeg;
        t.SelActive = _sel.HasSelection(_row, _col);
        if (t.SelActive) { t.AnchorRow = _sel.AnchorRow; t.AnchorCol = _sel.AnchorCol; }
    }

    /// <summary>Считать вид активной вкладки из модели.</summary>
    internal void LoadTabState()
    {
        DocTab t = _docs[_active];
        _row = t.Row; _col = t.Col; _desiredCol = t.DesiredCol;
        _top = t.Top; _left = t.Left; _topSeg = t.TopSeg;
        ClampCursor();
        _sel.Clear();
        if (t.SelActive)
            _sel.Start(t.AnchorRow, t.AnchorCol);
    }

    private void ApplySettings()
    {
        _loc = Loc.Load(_settings.Language);
        _theme = ThemeCatalog.Resolve(_settings, _settings.Theme);
    }

    private string DisplayError(Exception ex) => ex switch
    {
        InvalidOperationException { Message: "NoFileName" } => _loc["error.nofilename"],
        InvalidOperationException { Message: "ReadOnly" } => _loc["error.readonly"],
        _ => ex.Message,
    };

    public void Run()
    {
        Console.TreatControlCAsInput = true;
        Console.CursorVisible = false;
        _screen.TrueColor = Terminal.TryEnableVirtualTerminal();
        Terminal.TryEnableRawInput();

        try
        {
            try { Console.Write("\x1b[?2004h"); } catch (IOException) { }
            Render();
            MaybeRestore();
            if (_docs.Count == 1 && _buf.FilePath is null && !_buf.IsModified)
                SetMessage(_loc["msg.hint"]);
            while (!_quitRequested)
            {
                Render();
                InputEvent ev;
                try
                {
                    ev = InputReader.Read();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                HandleInput(ev);
                AutoDraft();
            }
            SaveSessionTabs();
        }
        finally
        {
            try { Console.Write("\x1b[?2004l"); } catch (IOException) { }
            Terminal.RestoreInput();
            Console.ResetColor();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }

    internal static bool DraftDue(DateTime last, DateTime now) =>
        (now - last).TotalSeconds >= 30;

    private void AutoDraft()
    {
        if (!_buf.IsModified || !DraftDue(_lastDraftAt, DateTime.Now))
            return;
        _lastDraftAt = DateTime.Now;
        try
        {
            _drafts.Write(_buf.FilePath, _buf.Lines, _row, _col);
        }
        catch
        {
        }
    }

    /// <summary>Аварийный сброс черновиков всех изменённых вкладок (для crash handler).
    /// Хендлеру падать нельзя: каждая запись и весь обход — в try/catch.</summary>
    internal int EmergencyDump()
    {
        int n = 0;
        try
        {
            SaveTabState(); // вид активной вкладки — из полей в модель
            foreach (Pane p in _panes)
                foreach (DocTab t in p.Docs)
                {
                    if (!t.Buf.IsModified)
                        continue;
                    try
                    {
                        _drafts.Write(t.Buf.FilePath, t.Buf.Lines, t.Row, t.Col);
                        n++;
                    }
                    catch
                    {
                    }
                }
        }
        catch
        {
        }
        return n;
    }

    private void MaybeRestore()
    {
        List<(string key, DocDraft draft)> all;
        try
        {
            all = _drafts.ReadAll();
        }
        catch
        {
            return;
        }
        if (all.Count == 0)
            return;
        _restoreDrafts.Clear();
        _restoreDrafts.AddRange(all.Take(20));
        _dialog = new ModalDialog(ModalState.Restore(_loc,
            _restoreDrafts.Select(d => (d.draft.File ?? _loc["status.noname"], d.draft.SavedAt)).ToList()),
            ApplyModalOutcome);
    }

    private void ApplyRestore(int button)
    {
        if (_restoreDrafts.Count == 0)
            return;
        var (key, d) = _restoreDrafts[Math.Clamp(button, 0, _restoreDrafts.Count - 1)];
        _restoreDrafts.Clear();
        if (d.File is not null)
        {
            try
            {
                _buf.Open(d.File);
            }
            catch
            {
                _buf.Clear();
            }
        }
        else
        {
            _buf.Clear();
        }
        _buf.RestoreContent(d.Lines);
        ResetCursor();
        _row = Math.Clamp(d.Row, 0, _buf.Count - 1);
        _col = Math.Min(Math.Max(d.Col, 0), _buf.GetLine(_row).Length);
        TrackCol();
        try
        {
            _drafts.DeleteKey(key); // восстановлен — дальше ведут автосейв/сейв/выход
        }
        catch
        {
        }
        SetMessage(_loc["msg.restored"]);
    }

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
        HandleKey(((KeyInput)input).Key);
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

    /// <summary>Файлы больше лимита — только через подтверждение (фриз подсветки).</summary>
    internal const long LargeFileBytes = 16 << 20;

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
    private static List<TopMenu> BuildMenus(Loc loc) => new()
    {
        new TopMenu(loc["menu.file"], 'F', new List<MenuItem>
        {
            new(loc["menu.newtab"], 'T', "Ctrl+T", EditorCommand.NewTab),
            new(loc["menu.open"], 'O', null, EditorCommand.OpenFile),
            new(loc["menu.recent"], 'R', null, EditorCommand.OpenRecent),
            new(loc["menu.save"], 'S', "^S", EditorCommand.Save),
            new(loc["menu.saveas"], 'A', "Ctrl+Shift+S", EditorCommand.SaveAs),
            new(loc["menu.saveall"], 'L', null, EditorCommand.SaveAll),
            MenuItem.Separator,
            new(loc["menu.format"], 'F', "F9", EditorCommand.FileFormat),
            new(loc["menu.closetab"], 'W', "Ctrl+W", EditorCommand.CloseTab),
            MenuItem.Separator,
            new(loc["menu.settings"], 'P', null, EditorCommand.Settings),
            new(loc["menu.exit"], 'X', "^Q", EditorCommand.Quit),
        }),
        new TopMenu(loc["menu.edit"], 'E', new List<MenuItem>
        {
            new(loc["menu.undo"], 'U', "^Z", EditorCommand.Undo),
            new(loc["menu.redo"], 'R', "^Y", EditorCommand.Redo),
            MenuItem.Separator,
            new(loc["menu.cut"], 'T', "^K", EditorCommand.CutLine),
            new(loc["menu.copy"], 'C', "^C", EditorCommand.CopyLine),
            new(loc["menu.paste"], 'P', "^U", EditorCommand.Paste),
            new(loc["menu.duplicate"], 'D', "^D", EditorCommand.DuplicateLine),
            new(loc["menu.togglecomment"], 'O', "Ctrl+/", EditorCommand.ToggleComment),
            new(loc["menu.sortlines"], 'S', null, EditorCommand.SortLines),
            MenuItem.Separator,
            new(loc["menu.gobracket"], 'J', "Alt+]", EditorCommand.GoBracketMatch),
            new(loc["menu.find"], 'F', "^F", EditorCommand.Find),
            new(loc["menu.replace"], 'H', "^H", EditorCommand.Replace),
            new(loc["menu.goto"], 'G', "^G", EditorCommand.GoToLine),
            new(loc["menu.grep"], 'E', "Ctrl+Shift+F", EditorCommand.Grep),
            MenuItem.Separator,
            new(loc["menu.trimtrail"], 'M', null, EditorCommand.TrimTrailing),
            new(loc["menu.selectall"], 'A', "^A", EditorCommand.SelectAll),
        }),
        new TopMenu(loc["menu.help"], 'H', new List<MenuItem>
        {
            new(loc["menu.helpitem"], 'H', "F1", EditorCommand.Help),
            new(loc["menu.about"], 'A', null, EditorCommand.About),
        }),
    };

    /// <summary>Исход закрытой модалки (pending-действия живут здесь).</summary>
    private void ApplyModalOutcome(ModalState m, ModalKeyOutcome o)
    {
        if (o.Cancelled)
        {
            _overwritePath = string.Empty;
            _pendingGrepRow = 0;
            _recentPaths.Clear();
            if (_pending != PendingOp.None)
            {
                _pending = PendingOp.None;
                SetMessage(_loc["msg.cancelled"]);
            }
            return;
        }
        switch (m.Kind, o.Button)
        {
            case (ModalKind.UnsavedQuit, var b):
                switch (MapUnsavedButton(b))
                {
                    case UnsavedAction.Save:
                        SaveFlowForPending();
                        return;
                    case UnsavedAction.Discard:
                        _drafts.Delete(_buf.FilePath);
                        DiscardBuffer();
                        ApplyPending();
                        return;
                    default:
                        _pending = PendingOp.None;
                        SetMessage(_loc["msg.cancelled"]);
                        return;
                }
            case (ModalKind.Overwrite, 0):
                try
                {
                    _buf.Save(_overwritePath, Backups);
                    TouchRecent(_buf.FilePath);
                    _drafts.Delete(_buf.FilePath);
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex)
                {
                    _overwritePath = string.Empty;
                    _pending = PendingOp.None;
                    _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.save"], DisplayError(ex)), ApplyModalOutcome);
                    return;
                }
                _overwritePath = string.Empty;
                ApplyPending();
                return;
            case (ModalKind.ReplaceConfirm, 0):
                DoReplace(_pendingReplaceTerm, _pendingReplaceRep);
                _pendingReplaceTerm = string.Empty;
                _pendingReplaceRep = string.Empty;
                return;
            case (ModalKind.ReplaceConfirm, _):
                _pendingReplaceTerm = string.Empty;
                _pendingReplaceRep = string.Empty;
                SetMessage(_loc["msg.cancelled"]);
                return;
            case (ModalKind.LargeFile, 0):
                ApplyPending(); // тот же маршрут PendingOp.Open, но уже с force
                return;
            case (ModalKind.LargeFile, _):
                _pending = PendingOp.None;
                SetMessage(_loc["msg.cancelled"]);
                return;
            case (ModalKind.Complete, var b):
                ApplyCompletion(_pendingCompletePrefix, m.Buttons[b].Label);
                _pendingCompletePrefix = string.Empty;
                return;
            case (ModalKind.Grep, var b):
                OpenGrepHit(b);
                return;
            case (ModalKind.Recent, var b):
                OpenRecentPick(b);
                return;
            case (ModalKind.Tabs, var b):
                SwitchTab(b);
                return;
            case (ModalKind.Restore, var b):
                ApplyRestore(b);
                return;
            default:
                _overwritePath = string.Empty;
                if (_pending != PendingOp.None)
                {
                    _pending = PendingOp.None;
                    SetMessage(_loc["msg.cancelled"]);
                }
                return;
        }
    }

    internal enum UnsavedAction { Save, Discard, Cancel }

    internal static UnsavedAction MapUnsavedButton(int button) => button switch
    {
        0 => UnsavedAction.Save,
        1 => UnsavedAction.Discard,
        _ => UnsavedAction.Cancel,
    };

    /// <summary>Сохранение из попапа с последующим отложенным действием.</summary>
    private void SaveFlowForPending()
    {
        try
        {
            switch (_buf.FilePath)
            {
                case null:
                    string? path = PickSavePath(_loc["status.untitled"]);
                    if (path is null)
                    {
                        _pending = PendingOp.None;
                        SetMessage(_loc["msg.cancelled"]);
                        return;
                    }
                    if (File.Exists(path))
                    {
                        _overwritePath = path;
                        _dialog = new ModalDialog(ModalState.Overwrite(_loc, Path.GetFileName(path)), ApplyModalOutcome);
                        return;
                    }
                    _buf.Save(path, Backups);
                    break;
                default:
                    _buf.Save(backup: Backups);
                    break;
            }
            TouchRecent(_buf.FilePath);
            _drafts.Delete(_buf.FilePath);
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
            ApplyPending();
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.save"], DisplayError(ex)), ApplyModalOutcome);
        }
    }

    private void ApplyPending()
    {
        PendingOp p = _pending;
        string path = _pendingPath;
        _pending = PendingOp.None;
        switch (p)
        {
            case PendingOp.Quit:
                if (AnyModified())
                {
                    ActivateFirstModified();
                    _pending = PendingOp.Quit;
                    _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
                }
                else
                {
                    _quitRequested = true;
                }
                break;
            case PendingOp.Open:
                LoadFile(path, force: true);
                if (_pendingGrepRow > 0)
                {
                    GoToLineNumber(_pendingGrepRow);
                    _pendingGrepRow = 0;
                }
                break;
            case PendingOp.CloseTab: CloseTabNow(); break;
            default: break;
        }
    }

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
                dlg.HandleKey(((KeyInput)ev).Key);
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

    internal const int ReplaceConfirmThreshold = 50;

    private void SetMessage(string m)
    {
        _message = m;
        _messageUntil = DateTime.Now.AddSeconds(4);
    }

    private string CurrentMessage =>
        DateTime.Now <= _messageUntil ? _message : string.Empty;

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
            var k = ((KeyInput)ev).Key;
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
