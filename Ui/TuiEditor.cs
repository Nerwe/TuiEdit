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
    private int _mouseX; // последняя позиция мыши (для hover)
    private int _mouseY;
    private bool _mouseActive; // мышь была последним вводом — hover вместо selection-подсветки
    private bool _mouseDrag; // идёт тяга с зажатой левой (press был в тексте)
    private int _mousePane; // панель начала тяги (фокус mid-drag не уезжает)
    private int _mouseRow; // курсор в момент press (якорь будущей тяги)
    private int _mouseCol;
    // Ввод читается через статический InputReader.Read (состояния нет).
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly CommandDispatcher _dispatcher;
    private Loc _loc;
    private Theme _theme;

    /// <summary>Версия из сборки (csproj Version); fallback — на случай ручной сборки.</summary>
    internal static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.4.0";

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
        _dispatcher = new CommandDispatcher(BuildCommandMap());
    }

    /// <summary>Command dispatcher (every <see cref="EditorCommand"/> except None must resolve).</summary>
    internal ICommandDispatcher Dispatcher => _dispatcher;

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
        ApplyMouseSetting();
    }

    /// <summary>Применить уровень мыши живьём: ввод, SGR, фокус и флаги консоли.</summary>
    private void ApplyMouseSetting()
    {
        InputReader.MouseLevel = _settings.Mouse;
        if (_settings.Mouse == MouseLevel.Off)
        {
            Terminal.DisableMouse();
            Terminal.DisableFocusTracking();
        }
        else
        {
            Terminal.SetMouseLevel(_settings.Mouse);
            Terminal.TryEnableFocusTracking(); // нужен restore DEC-режимов по focus-in
        }
        Terminal.ApplyMouseInput(_settings.Mouse != MouseLevel.Off);
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
            ApplyMouseSetting();
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
            Terminal.DisableMouse();
            Terminal.DisableFocusTracking();
            Terminal.RestoreInput();
            Console.ResetColor();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }

    /// <summary>Файлы больше лимита — только через подтверждение (фриз подсветки).</summary>
    internal const long LargeFileBytes = 16 << 20;

    internal enum UnsavedAction { Save, Discard, Cancel }

    internal const int ReplaceConfirmThreshold = 50;

    private void SetMessage(string m)
    {
        _message = m;
        _messageUntil = DateTime.Now.AddSeconds(4);
    }

}
