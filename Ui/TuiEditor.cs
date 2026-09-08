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

    /// <summary>Новая пустая вкладка (чистую безымянную не дублируем).</summary>
    internal void NewTab()
    {
        if (_buf.FilePath is null && !_buf.IsModified)
            return;
        SaveTabState();
        _docs.Add(new DocTab(new TextBuffer(null)));
        _active = _docs.Count - 1;
        LoadTabState();
    }

    /// <summary>Открыть вкладки прошлой сессии (несуществующие пропускаем).</summary>
    internal int RestoreSessionTabs()
    {
        if (!_settings.RestoreSession || _settings.SessionTabs.Count == 0)
            return 0;
        int n = 0;
        foreach (SessionTab t in _settings.SessionTabs)
        {
            if (string.IsNullOrWhiteSpace(t.Path) || !File.Exists(t.Path))
                continue;
            try
            {
                if (n == 0 && _docs.Count == 1 && _buf.FilePath is null && !_buf.IsModified)
                    _buf.Open(t.Path);
                else
                {
                    SaveTabState();
                    _docs.Add(new DocTab(new TextBuffer(t.Path)));
                    _active = _docs.Count - 1;
                    LoadTabState();
                }
                _row = Math.Clamp(t.Row, 0, _buf.Count - 1);
                _col = Math.Clamp(t.Col, 0, _buf.GetLine(_row).Length);
                TrackCol();
                SaveTabState();
                TouchRecent(t.Path);
                n++;
            }
            catch
            {
            }
        }
        return n;
    }

    /// <summary>Запомнить открытые файлы с курсорами для следующего старта.</summary>
    internal void SaveSessionTabs()
    {
        if (!_settings.RestoreSession)
            return;
        SaveTabState();
        var tabs = new List<SessionTab>();
        foreach (Pane p in _panes)
        {
            foreach (DocTab t in p.Docs)
            {
                if (t.Buf.FilePath is null || !File.Exists(t.Buf.FilePath))
                    continue;
                int row = Math.Clamp(t.Row, 0, t.Buf.Count - 1);
                tabs.Add(new SessionTab(t.Buf.FilePath, row, Math.Max(0, t.Col)));
                if (tabs.Count >= AppSettings.MaxSessionTabs)
                    break;
            }
            if (tabs.Count >= AppSettings.MaxSessionTabs)
                break;
        }
        _settings.SessionTabs = tabs;
        _store.Save(_settings);
    }

    /// <summary>Переключиться на вкладку (по кругу).</summary>
    internal void SwitchTab(int index)
    {
        if (_docs.Count == 0)
            return;
        SaveTabState();
        _active = ((index % _docs.Count) + _docs.Count) % _docs.Count;
        LoadTabState();
    }

    /// <summary>Закрыть активную вкладку (грязную — через диалог).</summary>
    internal void CloseTab()
    {
        if (_buf.IsModified)
        {
            _pending = PendingOp.CloseTab;
            _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
            return;
        }
        CloseTabNow();
    }

    /// <summary>Имя грязного буфера для вопроса.</summary>
    private string DirtyLabel() =>
        _buf.FilePath is null ? _loc["status.untitled"] : Path.GetFileName(_buf.FilePath);

    /// <summary>Закрыть активную вкладку безусловно (последняя в панели — чистит панель).</summary>
    internal void CloseTabNow()
    {
        if (_docs.Count <= 1)
        {
            if (_panes.Count > 1)
            {
                _panes.RemoveAt(_pane);
                _pane = Math.Min(_pane, _panes.Count - 1);
                LoadTabState();
                return;
            }
            ClearDoc();
            return;
        }
        _docs.RemoveAt(_active);
        _active = Math.Min(_active, _docs.Count - 1);
        LoadTabState();
    }

    private void ListTabs()
    {
        _dialog = new ModalDialog(
            ModalState.Tabs(_loc, _docs.Select(d => d.TabTitle(_loc)).ToList()),
            ApplyModalOutcome);
    }

    private bool AnyModified() => _panes.Any(p => p.Docs.Any(d => d.Buf.IsModified));

    /// <summary>Первая грязная вкладка — активной (для цикла выхода).</summary>
    private void ActivateFirstModified()
    {
        for (int pi = 0; pi < _panes.Count; pi++)
            for (int i = 0; i < _panes[pi].Docs.Count; i++)
                if (_panes[pi].Docs[i].Buf.IsModified)
                {
                    SaveTabState();
                    _pane = pi;
                    _active = i;
                    LoadTabState();
                    return;
                }
    }

    /// <summary>Разделить вид: новая панель справа, фокус — в неё.</summary>
    internal void SplitPane()
    {
        SaveTabState();
        _panes.Add(new Pane(new DocTab(new TextBuffer(null))));
        _pane = _panes.Count - 1;
        LoadTabState();
    }

    /// <summary>Фокус на панель (по кругу).</summary>
    internal void SwitchPane(int index)
    {
        if (_panes.Count == 0)
            return;
        SaveTabState();
        _pane = ((index % _panes.Count) + _panes.Count) % _panes.Count;
        LoadTabState();
    }

    /// <summary>Ширины панелей: поровну, остаток — левым.</summary>
    internal static int[] PaneWidths(int total, int count)
    {
        if (count <= 0)
            return [];
        int[] r = new int[count];
        int each = total / count, rest = total % count;
        for (int i = 0; i < count; i++)
            r[i] = each + (i < rest ? 1 : 0);
        return r;
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

    /// <summary>Отметить файл недавним и сохранить настройки.</summary>
    private void TouchRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _settings.TouchRecent(path);
        _store.Save(_settings);
    }

    /// <summary>Сохранить все именованные грязные вкладки всех панелей.</summary>
    internal void SaveAll()
    {
        SaveTabState();
        int savedPane = _pane, savedActive = _active;
        int saved = 0, skipped = 0;
        string? error = null;
        try
        {
            for (int i = 0; i < _panes.Count; i++)
            {
                _pane = i;
                for (int d = 0; d < _panes[i].Docs.Count; d++)
                {
                    _active = d;
                    LoadTabState();
                    if (!_buf.IsModified)
                        continue;
                    if (_buf.FilePath is null)
                    {
                        skipped++;
                        continue;
                    }
                    try
                    {
                        _buf.Save(backup: Backups);
                        TouchRecent(_buf.FilePath);
                        _drafts.Delete(_buf.FilePath);
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        error = $"{_loc["error.save"]}: {DisplayError(ex)}";
                    }
                }
            }
        }
        finally
        {
            _pane = savedPane;
            _active = savedActive;
            LoadTabState();
        }
        SetMessage(error ?? _loc.Format("msg.savedall", saved, skipped));
    }

    private void Save()
    {
        switch (_buf.FilePath)
        {
            case null:
                SaveAs();
                return;
            default:
                try
                {
                    _buf.Save(backup: Backups);
                    TouchRecent(_buf.FilePath);
                    _drafts.Delete(_buf.FilePath);
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex) { SetMessage($"{_loc["error.save"]}: {DisplayError(ex)}"); }
                return;
        }
    }

    private void SaveAs()
    {
        string? path = PickSavePath(
            _buf.FilePath is null ? _loc["status.untitled"] : Path.GetFileName(_buf.FilePath));
        if (path is null) { SetMessage(_loc["msg.cancelled"]); return; }
        if (File.Exists(path))
        {
            _overwritePath = path;
            _dialog = new ModalDialog(ModalState.Overwrite(_loc, Path.GetFileName(path)), ApplyModalOutcome);
            return;
        }
        try
        {
            _buf.Save(path, Backups);
            TouchRecent(_buf.FilePath);
            _drafts.Delete(_buf.FilePath);
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
        }
        catch (Exception ex) { SetMessage($"{_loc["error.save"]}: {DisplayError(ex)}"); }
    }

    private void TryQuit()
    {
        if (!AnyModified())
        {
            _quitRequested = true;
            return;
        }
        // Красный попап Save / Don't save / Cancel, как unsaved-changes в MS Edit.
        _pending = PendingOp.Quit;
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
    }

    private void DoOpen()
    {
        string? path = RunPicker(PickerMode.Open, StartDir(), string.Empty);
        if (path is null)
        {
            SetMessage(_loc["msg.cancelled"]);
            return;
        }
        OpenPicked(path);
    }

    /// <summary>Открыть выбранный путь: сразу или через диалог несохранённых.</summary>
    private void OpenPicked(string path)
    {
        if (!_buf.IsModified)
        {
            LoadFile(path);
            return;
        }
        _pending = PendingOp.Open;
        _pendingPath = path;
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
    }

    /// <summary>Панель файлов: открыть/закрыть/вернуть фокус (корень — папка файла).</summary>
    private void ToggleSidebar()
    {
        if (_sidebar is null)
        {
            string root = _buf.FilePath is null
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(Path.GetFullPath(_buf.FilePath)) ?? Directory.GetCurrentDirectory();
            _sidebar = new SidebarState(root);
            _sidebarFocus = true;
            return;
        }
        if (_sidebarFocus)
        {
            _sidebar = null;
            _sidebarFocus = false;
            return;
        }
        _sidebarFocus = true;
    }

    /// <summary>Клавиша при фокусе в панели.</summary>
    private void HandleSidebarKey(ConsoleKeyInfo k)
    {
        if (_sidebar is null)
        {
            _sidebarFocus = false;
            return;
        }
        bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        if (ctrl && k.Key == ConsoleKey.B) { ToggleSidebar(); return; }
        if (ctrl || (k.Modifiers & ConsoleModifiers.Alt) != 0)
            return;
        switch (k.Key)
        {
            case ConsoleKey.Escape: _sidebarFocus = false; return;
            case ConsoleKey.UpArrow: _sidebar.MoveHighlight(-1, TextHeight()); return;
            case ConsoleKey.DownArrow: _sidebar.MoveHighlight(1, TextHeight()); return;
            case ConsoleKey.Home: _sidebar.MoveHighlight(int.MinValue / 2, TextHeight()); return;
            case ConsoleKey.End: _sidebar.MoveHighlight(int.MaxValue / 2, TextHeight()); return;
            case ConsoleKey.PageUp: _sidebar.MoveHighlight(-Math.Max(1, TextHeight() - 1), TextHeight()); return;
            case ConsoleKey.PageDown: _sidebar.MoveHighlight(Math.Max(1, TextHeight() - 1), TextHeight()); return;
            case ConsoleKey.Enter:
                if (_sidebar.EnterSelected())
                    return;
                string? path = _sidebar.SelectedPath;
                if (path is null)
                    return;
                _sidebarFocus = false;
                OpenPicked(path);
                return;
        }
    }

    private void DoRecent()
    {
        _settings.PruneRecent();
        _store.Save(_settings);
        if (_settings.RecentFiles.Count == 0)
        {
            SetMessage(_loc["msg.recent.empty"]);
            return;
        }
        _recentPaths.Clear();
        _recentPaths.AddRange(_settings.RecentFiles);
        _dialog = new ModalDialog(ModalState.Recent(_loc, _recentPaths), ApplyModalOutcome);
    }

    private void OpenRecentPick(int button)
    {
        if (_recentPaths.Count == 0)
            return;
        string path = _recentPaths[Math.Clamp(button, 0, _recentPaths.Count - 1)];
        _recentPaths.Clear();
        OpenPicked(path);
    }

    private void ClearDoc()
    {
        _buf.Clear();
        ResetCursor();
        SetMessage(_loc["msg.newdoc"]);
    }

    /// <summary>
    /// «Не сохранять»: откат к последнему сохранённому (файл перечитывается,
    /// безымянный очищается). Без отката цикл выхода возвращается к вкладке снова.
    /// </summary>
    private void DiscardBuffer()
    {
        try
        {
            if (_buf.FilePath is not null && File.Exists(_buf.FilePath))
                _buf.Open(_buf.FilePath);
            else
                _buf.Clear();
        }
        catch
        {
            _buf.Clear();
        }
        _sel.Clear();
        ClampCursor();
    }

    private void ResetCursor()
    {
        _row = _col = _desiredCol = _top = _left = _topSeg = 0;
    }

    /// <summary>Файлы больше лимита — только через подтверждение (фриз подсветки).</summary>
    internal const long LargeFileBytes = 16 << 20;

    private void LoadFile(string path, bool force = false)
    {
        bool existed = File.Exists(path);
        if (!force && existed && GuardLargeFile(path))
            return;
        try
        {
            _buf.Open(path);
            ResetCursor();
            TouchRecent(path);
            SetMessage(existed ? _loc.Format("msg.opened", path) : _loc.Format("msg.newfile", path));
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.open"], DisplayError(ex)), ApplyModalOutcome);
        }
    }

    /// <summary>Проверка размера: большой файл — диалог, путь откладывается в pending.</summary>
    private bool GuardLargeFile(string path)
    {
        long bytes;
        try { bytes = new FileInfo(path).Length; }
        catch { return false; } // размер не узнали — пусть открывает, ошибку покажет Open
        if (bytes <= LargeFileBytes)
            return false;
        _pending = PendingOp.Open;
        _pendingPath = path;
        long mb = bytes >> 20;
        _dialog = new ModalDialog(
            ModalState.LargeFile(_loc, Path.GetFileName(path), mb, LargeFileBytes >> 20),
            ApplyModalOutcome);
        return true;
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

    private string StartDir()
    {
        try
        {
            if (_buf.FilePath is not null)
            {
                string? d = Path.GetDirectoryName(Path.GetFullPath(_buf.FilePath));
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
                    return d;
            }
        }
        catch
        {
        }
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return OperatingSystem.IsWindows() ? "C:\\" : "/";
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

    private void EnsureVisible(int textHeight, int contentWidth)
    {
        ClampCursor();
        if (FoldHidden(_row))
            UnfoldPath(); // страховка: курсор всегда на видимой строке
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
        _left = 0; // переносы вместо горизонтального скролла
        if (_row != _top) _topSeg = 0;
        if (_row < _top) { _top = _row; _topSeg = 0; }
        int rows = CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, true, textHeight, folds);
        if (rows < 0) // курсор выше видимого (сдвиг внутри длинной строки)
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
                // Курсор на дальнем сегменте длинной строки — показываем её хвост.
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
    /// Визуальная строка курсора: сегменты [top, row) минус прокрученные плюс сегмент курсора.
    /// Точность выше cap не гарантируется (экрану достаточно cap=textHeight).
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
            rows += WordWrap.SegmentCount(lines[r], contentWidth);
        }
        if (rows >= cap) return rows;
        return rows + CursorSeg(lines[row], col, contentWidth);
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

        int sideW = _sidebar is null ? 0 : SidebarState.Width;
        int[] paneWs = PaneWidths(w - sideW, _panes.Count);
        int[] paneXs = new int[_panes.Count];
        for (int i = 0, x = sideW; i < paneXs.Length; i++)
        {
            paneXs[i] = x;
            x += paneWs[i];
        }
        int tabH = _panes.Any(p => p.Docs.Count > 1) ? 1 : 0;
        int textHeight = h - 2 - tabH;
        int y0 = 1 + tabH;
        bool wrap = _settings.WordWrap;
        int aNumWidth = Math.Max(4, _buf.Count.ToString(CultureInfo.InvariantCulture).Length);
        int aGutter = _settings.ShowLineNumbers ? aNumWidth + 4 : 0;
        int activeCw = Math.Max(1, paneWs[_pane] - aGutter);

        EnsureVisible(textHeight, activeCw);
        if (_screen.Width != w || _screen.Height != h)
            _screen.Resize(w, h);

        // Меню-бар (строка 0).
        DrawMenuBar(w);

        // Неактивные панели — из снапшотов вкладок, активная — из полей (кэш).
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
        DrawSidebar(y0, textHeight);
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
            GitStatus.ForFile(_buf.FilePath), _active, _docs.Count, _pane, _panes.Count);
        _screen.Text(0, h - 1, StatusBar.Build(left, right, w), _theme.StatusFg, _theme.StatusBg);

        // Поверх текста: раскрытое меню и активное диалоговое окно.
        DrawDropdown(w, h);
        _dialog?.Draw(_screen, _theme, _loc);

        _screen.Flush();

        // Аппаратный курсор — один раз за кадр (прячем под меню, диалогом, панелью).
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
    /// Текст посимвольно: цвет ячейки — выделение / поиск / текущая строка / синтаксис.
    /// x0 — сдвиг на ширину панели, y0 — первая строка текста.
    /// </summary>
    private void DrawText(int x0, int y0, int w, int textHeight, int contentWidth, int gutterWidth, int numWidth, bool wrap)
    {
        int y = y0;
        int fileLine = _top;
        int firstSeg = _topSeg;
        CompiledGrammar? grammar = CurrentGrammar();
        var bracket = BracketPair();
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
                _docs[_active].Highlighter.GetLine(_buf, grammar, fileLine);
            int synIdx = 0;
            List<int> starts = wrap ? WordWrap.SegmentStarts(line, contentWidth) : new List<int> { 0 };
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
                    _screen.Text(x0 + 1, y, num + " │ ", _theme.GutterFg, _theme.EditorBg);
                }
                bool[] isMatch = FindMatches(TabStops.Slice(line, @base, contentWidth),
                    EffectiveSearchTerm, _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
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
                // Хвост строки — пробелы обычным цветом.
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
    /// <summary>
    /// Панель файлов слева: заголовок + список со скроллом.
    /// Типы цветом: папки, «..», скрытые, исполняемые, файлы.
    /// </summary>
    private void DrawSidebar(int y0, int textHeight)
    {
        if (_sidebar is null)
            return;
        int sw = SidebarState.Width;
        int inner = sw - 1;
        for (int y = 1; y < y0; y++)
            _screen.Text(0, y, new string(' ', inner) + "│", _theme.EditorFg, _theme.EditorBg);
        string title = "▸ " + Path.GetFileName(
            _sidebar.CurrentDir.TrimEnd(Path.DirectorySeparatorChar));
        if (title.Length > inner)
            title = "…" + title[^(inner - 1)..];
        _screen.Text(0, y0, title.PadRight(inner)[..inner] + "│", _theme.MenuOpenFg, _theme.MenuOpenBg);
        int visCount = Math.Max(1, textHeight - 1);
        int vis = Math.Min(visCount, _sidebar.Entries.Count - _sidebar.Top);
        for (int vi = 0; vi < vis; vi++)
        {
            int i = _sidebar.Top + vi;
            SidebarEntry e = _sidebar.Entries[i];
            string label = (e.IsDir ? "+ " : "  ") + e.Name;
            if (label.Length > inner)
                label = label[..(inner - 1)] + "…";
            if (vi == 0 && _sidebar.Top > 0)
                label = label[..^1] + "↑";
            if (vi == vis - 1 && _sidebar.Top + vis < _sidebar.Entries.Count)
                label = label[..^1] + "↓";
            string cell = label.PadRight(inner)[..inner] + "│";
            int row = y0 + 1 + vi;
            if (i == _sidebar.Selected)
                _screen.Text(0, row, cell, _theme.ButtonSelFg, _theme.ButtonSelBg);
            else
                _screen.Text(0, row, cell, EntryFg(_theme, e), _theme.EditorBg);
        }
        for (int row = y0 + 1 + vis; row < y0 + textHeight; row++)
            _screen.Text(0, row, new string(' ', inner) + "│", _theme.EditorFg, _theme.EditorBg);
    }

    /// <summary>Цвет записи сайдбара по типу (выбранная красится отдельно).</summary>
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
    /// Строка вкладок в регионе панели: активная подсвечена, у грязных — «*»,
    /// длинный ряд — окном (активная всегда видна). Чужая панель — приглушена.
    /// </summary>
    private void DrawTabs(int px, int pw, bool focused)
    {
        if (_docs.Count <= 1)
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

    /// <summary>Начало видимого окна вкладок: сдвигаем, пока активная не влезет.</summary>
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

    private static bool[] FindMatches(string expanded, string term, bool matchCase, bool wholeWord, bool useRegex)
    {
        bool[] m = new bool[expanded.Length];
        if (string.IsNullOrEmpty(term))
            return m;
        if (useRegex)
        {
            Regex? rx = TextBuffer.TryBuildRegex(term, matchCase, wholeWord);
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
            bool sel = i == _menu.SelectedIndex;
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
