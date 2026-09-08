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

    /// <summary>Файлы больше лимита — только через подтверждение (фриз подсветки).</summary>
    internal const long LargeFileBytes = 16 << 20;

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

    internal const int ReplaceConfirmThreshold = 50;

    private void SetMessage(string m)
    {
        _message = m;
        _messageUntil = DateTime.Now.AddSeconds(4);
    }

    private string CurrentMessage =>
        DateTime.Now <= _messageUntil ? _message : string.Empty;

}
