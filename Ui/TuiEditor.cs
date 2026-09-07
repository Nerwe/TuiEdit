// TUI-движок на чистом System.Console.
// Используемые API из Microsoft Learn (System.Console):
// ReadKey(true), SetCursorPosition, WindowWidth/WindowHeight, CursorVisible,
// ForegroundColor/BackgroundColor, TreatControlCAsInput, OutputEncoding, Clear, ResetColor.

namespace TuiEdit;

internal sealed class TuiEditor
{
    private readonly TextBuffer _buf;
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
    private bool _quitRequested;
    private readonly TextSelection _sel = new();
    private MenuState? _menu;   // null — меню-бар закрыт
    private ModalState? _modal; // null — попапа нет
    private PendingOp _pending = PendingOp.None;
    private string _pendingPath = string.Empty;
    private string _overwritePath = string.Empty; // путь из модалки перезаписи
    private readonly List<int> _menuX = new(); // x-координаты меню в баре (из рендера)
    private FilePickerState? _picker; // null — менеджер закрыт
    private int _pickerCursorX = -1;  // курсор поля имени (из рендера)
    private int _pickerCursorY = -1;
    private readonly Screen _screen = new(); // кадр + diff-вывод (без мигания)
    private readonly InputReader _input = new();
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private Loc _loc;
    private Theme _theme;
    private bool _settingsOpen; // диалог настроек открыт
    private SettingsDialogState? _settingsDlg; // состояние диалога (рисуется внутри Render)
    private bool _helpOpen;   // экран справки открыт (ловушка ввода)
    private int _helpScroll;  // прокрутка справки

    private const string AppVersion = "0.1.0";

    /// <summary>Отложенное действие после диалога «несохранённые изменения».</summary>
    private enum PendingOp { None, Quit, New, Open }

    public TuiEditor(TextBuffer buf, AppSettings settings, SettingsStore store)
    {
        _buf = buf;
        _settings = settings;
        _store = store;
        _loc = Loc.Load(settings.Language);
        _theme = Themes.Get(settings.Theme);
    }

    /// <summary>Перечитать тему и язык из настроек (после диалога настроек).</summary>
    private void ApplySettings()
    {
        _loc = Loc.Load(_settings.Language);
        _theme = Themes.Get(_settings.Theme);
    }

    /// <summary>Текст ошибки для показа (технические коды маппим в Loc).</summary>
    private string DisplayError(Exception ex) => ex switch
    {
        InvalidOperationException { Message: "NoFileName" } => _loc["error.nofilename"],
        _ => ex.Message,
    };

    public void Run()
    {
        Console.TreatControlCAsInput = true;
        Console.CursorVisible = false;
        _screen.TrueColor = Terminal.TryEnableVirtualTerminal();

        try
        {
            // Bracketed paste (как в MS Edit: "\x1b[?2004h"): вставка из обмена
            // приходит одним событием и кладётся полным текстом, а не посимвольно.
            try { Console.Write("\x1b[?2004h"); } catch (IOException) { }
            Render(); // первичная отрисовка
            while (!_quitRequested)
            {
                Render();
                InputEvent ev;
                try
                {
                    ev = _input.Read();
                }
                catch (InvalidOperationException)
                {
                    // Ввод перенаправлен — работать не можем (см. docs System.Console).
                    return;
                }
                HandleInput(ev);
            }
        }
        finally
        {
            try { Console.Write("\x1b[?2004l"); } catch (IOException) { }
            Console.ResetColor();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }

    private void HandleInput(InputEvent input)
    {
        if (input is PasteInput paste)
        {
            if (_modal is not null)
                return; // модалки глотают ввод
            _menu = null;
            DeleteSelection();
            ClampCursor();
            (_row, _col) = _buf.InsertText(_row, _col, paste.Text);
            ClampCursor();
            TrackCol();
            return;
        }
        HandleKey(((KeyInput)input).Key);
    }

    // ---------- Ввод ----------

    private void HandleKey(ConsoleKeyInfo k)
    {
        // Экран справки глотает весь ввод (как модалка).
        if (_helpOpen)
        {
            HandleHelpKey(k);
            return;
        }
        // Модалка глотает весь ввод (как modal_end в MS Edit).
        if (_modal is not null)
        {
            ApplyModalKey(k);
            return;
        }

        // Открытое меню перехватывает ввод (см. menubar_menu_end в MS Edit).
        if (_menu is not null)
        {
            HandleMenuKey(k);
            return;
        }

        // Esc гасит активное выделение.
        if (k.Key == ConsoleKey.Escape && _sel.HasSelection(_row, _col))
        {
            _sel.Clear();
            return;
        }

        EditorCommand cmd = KeyMap.Map(k);

        // Shift+движение расширяет выделение, движение без Shift — снимает.
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

    /// <summary>
    /// Выполняет команду редактора. Маршрутизация клавиш — через чистый KeyMap
    /// (см. KeyMap.cs), чтобы маппинг проверялся unit-тестами без консоли.
    /// </summary>
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
            case EditorCommand.NewFile: DoNew(); return;
            case EditorCommand.OpenFile: DoOpen(); return;
            case EditorCommand.About: _modal = ModalState.About(_loc, AppVersion); return;
            case EditorCommand.Help: _helpOpen = true; _helpScroll = 0; return;
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
            case EditorCommand.Settings: RunSettings(); return;
            case EditorCommand.Find: Find(); return;
            case EditorCommand.FindNext: FindNext(); return;
            case EditorCommand.FindPrev: FindPrev(); return;
            case EditorCommand.Replace: Replace(); return;
            case EditorCommand.GoToLine: GoToLine(); return;
            case EditorCommand.CutLine: CutLine(); return;
            case EditorCommand.CopyLine: CopyLine(); return;
            case EditorCommand.Paste: Paste(); return;
            case EditorCommand.DuplicateLine: DuplicateBlock(); return;
            case EditorCommand.MoveLineUp: MoveLineBlock(-1); return;
            case EditorCommand.MoveLineDown: MoveLineBlock(1); return;
            case EditorCommand.SaveAs: SaveAs(); return;
            case EditorCommand.Undo: _buf.Undo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.undo"]); return;
            case EditorCommand.Redo: _buf.Redo(); _sel.Clear(); ClampCursor(); SetMessage(_loc["msg.redo"]); return;
            case EditorCommand.InsertEnter:
                DeleteSelection(); // замена выделения
                (_row, _col) = _buf.SplitLine(_row, _col);
                TrackCol();
                return;
            case EditorCommand.InsertBackspace:
                if (DeleteSelection()) return; // стереть выделение вместо символа
                (_row, _col) = _buf.Backspace(_row, _col);
                TrackCol();
                return;
            case EditorCommand.InsertDelete:
                if (DeleteSelection()) return; // стереть выделение вместо символа
                (_row, _col) = _buf.Delete(_row, _col);
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

    // ---------- Движения ----------

    private string CurLine => _buf.GetLine(_row);

    /// <summary>
    /// Запоминает визуальную колонку курсора для Up/Down
    /// (в символах нельзя — табы разной ширины).
    /// </summary>
    private void TrackCol() => _desiredCol = TabStops.VisualWidth(_buf.GetLine(_row), _col);

    /// <summary>Удаляет выделение (если есть), курсор — в его начало.</summary>
    /// <returns>Было ли выделение.</returns>
    private bool DeleteSelection()
    {
        if (!_sel.HasSelection(_row, _col))
            return false;
        var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
        (_row, _col) = _buf.DeleteRange(sr, sc, er, ec);
        _sel.Clear();
        TrackCol();
        return true;
    }

    /// <summary>Длина диапазона в символах (без учёта переводов строк).</summary>
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

    /// <summary>Tab по выделению: отступ каждой затронутой строке, выделение сохраняется.</summary>
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

    /// <summary>Shift+Tab: снять отступ со строк выделения (или с текущей).</summary>
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

    /// <summary>Выделить весь документ.</summary>
    private void SelectAll()
    {
        _sel.Start(0, 0);
        _row = _buf.Count - 1;
        _col = _buf.GetLine(_row).Length;
        TrackCol();
        SetMessage(_loc.Format("status.sel", SelectionLength(0, 0, _row, _col)));
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
            case (> 0, _): _col--; break;
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
        if (_row > 0) { _row--; _col = TabStops.CharIndexAtVisual(_buf.GetLine(_row), _desiredCol); }
    }

    private void MoveDown()
    {
        if (_row < _buf.Count - 1) { _row++; _col = TabStops.CharIndexAtVisual(_buf.GetLine(_row), _desiredCol); }
    }

    private void GoHome() { ClampCursor(); _col = 0; _desiredCol = 0; }
    private void GoEnd() { ClampCursor(); _col = CurLine.Length; TrackCol(); }
    private void GoDocStart() { _row = 0; _col = 0; _desiredCol = 0; }
    private void GoDocEnd() { _row = _buf.Count - 1; _col = _buf.GetLine(_row).Length; TrackCol(); }

    private void MovePage(int dir)
    {
        int h = TextHeight();
        _row = Math.Clamp(_row + dir * Math.Max(1, h - 1), 0, _buf.Count - 1);
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
            (_row, _col) = _buf.Backspace(_row, _col); // склейка строк
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
            (_row, _col) = _buf.Delete(_row, _col); // склейка строк
            TrackCol();
            return;
        }
        int end = WordMotion.Forward(line, _col);
        (_row, _col) = _buf.DeleteRange(_row, _col, _row, end);
        TrackCol();
    }

    // ---------- Команды ----------

    private void Save()
    {
        switch (_buf.FilePath)
        {
            case null:
                SaveAs(); // без имени — запросить, как Save as
                return;
            default:
                try
                {
                    _buf.Save();
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
            _modal = ModalState.Overwrite(_loc, Path.GetFileName(path));
            return;
        }
        try
        {
            _buf.Save(path);
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
        }
        catch (Exception ex) { SetMessage($"{_loc["error.save"]}: {DisplayError(ex)}"); }
    }

    private void TryQuit()
    {
        if (!_buf.IsModified)
        {
            _quitRequested = true;
            return;
        }
        // Красный попап Save / Don't save / Cancel, как unsaved-changes в MS Edit.
        _pending = PendingOp.Quit;
        _modal = ModalState.UnsavedQuit(_loc);
    }

    /// <summary>Новый документ (с диалогом при несохранённых изменениях).</summary>
    private void DoNew()
    {
        if (!_buf.IsModified)
        {
            ClearDoc();
            return;
        }
        _pending = PendingOp.New;
        _modal = ModalState.UnsavedQuit(_loc);
    }

    /// <summary>Открыть файл через менеджер, затем диалог при несохранённых изменениях.</summary>
    private void DoOpen()
    {
        string? path = RunPicker(PickerMode.Open, StartDir(), string.Empty);
        if (path is null)
        {
            SetMessage(_loc["msg.cancelled"]);
            return;
        }
        if (!_buf.IsModified)
        {
            LoadFile(path);
            return;
        }
        _pending = PendingOp.Open;
        _pendingPath = path;
        _modal = ModalState.UnsavedQuit(_loc);
    }

    private void ClearDoc()
    {
        _buf.Clear();
        ResetCursor();
        SetMessage(_loc["msg.newdoc"]);
    }

    private void ResetCursor()
    {
        _row = _col = _desiredCol = _top = _left = _topSeg = 0;
    }

    private void LoadFile(string path)
    {
        bool existed = File.Exists(path);
        try
        {
            _buf.Open(path);
            ResetCursor();
            SetMessage(existed ? _loc.Format("msg.opened", path) : _loc.Format("msg.newfile", path));
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _modal = ModalState.Error(_loc, _loc["error.open"], DisplayError(ex));
        }
    }

    /// <summary>Маршрут клавиши при открытом меню: навигация, хоткеи, активация.</summary>
    private void HandleMenuKey(ConsoleKeyInfo k)
    {
        // Esc / F10 — закрыть меню.
        if (k.Key is ConsoleKey.Escape or ConsoleKey.F10)
        {
            _menu = null;
            return;
        }
        // Alt+буква — переключиться на другое меню.
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
                _menu!.Open(_menu.OpenIndex); // сброс выбора на первый пункт
                return;
            case ConsoleKey.Enter:
                ActivateMenuItem(_menu!.Selected);
                return;
        }
        // Одиночная буква — хоткей пункта (позиционно, как в MS Edit).
        if ((k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && k.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            MenuItem? item = _menu!.FindItemByHotkey((char)k.Key);
            if (item is not null)
                ActivateMenuItem(item);
            return;
        }
        // Всё остальное (Ctrl-шорткаты, F2/F3...) — глобально: закрыть меню и выполнить.
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

    /// <summary>
    /// Меню-бар в стиле MS Edit: File / Edit / Help.
    /// Состав пунктов повторяет draw_menubar.rs (New/Open/Save/SaveAs/Exit и т.д.).
    /// </summary>
    private static List<TopMenu> BuildMenus(Loc loc) => new()
    {
        new TopMenu(loc["menu.file"], 'F', new List<MenuItem>
        {
            new(loc["menu.new"], 'N', null, EditorCommand.NewFile),
            new(loc["menu.open"], 'O', null, EditorCommand.OpenFile),
            new(loc["menu.save"], 'S', "F2", EditorCommand.Save),
            new(loc["menu.saveas"], 'A', "^O", EditorCommand.SaveAs),
            new(loc["menu.settings"], 'P', null, EditorCommand.Settings),
            new(loc["menu.exit"], 'X', "^Q", EditorCommand.Quit),
        }),
        new TopMenu(loc["menu.edit"], 'E', new List<MenuItem>
        {
            new(loc["menu.undo"], 'U', "^Z", EditorCommand.Undo),
            new(loc["menu.redo"], 'R', "^Y", EditorCommand.Redo),
            new(loc["menu.cut"], 'T', "^K", EditorCommand.CutLine),
            new(loc["menu.copy"], 'C', "^C", EditorCommand.CopyLine),
            new(loc["menu.paste"], 'P', "^U", EditorCommand.Paste),
            new(loc["menu.duplicate"], 'D', "^D", EditorCommand.DuplicateLine),
            new(loc["menu.find"], 'F', "^F", EditorCommand.Find),
            new(loc["menu.replace"], 'H', "^H", EditorCommand.Replace),
            new(loc["menu.goto"], 'G', "^G", EditorCommand.GoToLine),
            new(loc["menu.selectall"], 'A', "^A", EditorCommand.SelectAll),
        }),
        new TopMenu(loc["menu.help"], 'H', new List<MenuItem>
        {
            new(loc["menu.helpitem"], 'H', "F1", EditorCommand.Help),
            new(loc["menu.about"], 'A', null, EditorCommand.About),
        }),
    };

    /// <summary>Маршрут клавиши при открытом попапе.</summary>
    private void ApplyModalKey(ConsoleKeyInfo k)
    {
        ModalKeyOutcome o = _modal!.HandleKey(k);
        if (!o.Done)
            return;
        ModalState m = _modal!;
        _modal = null;
        if (o.Cancelled)
        {
            // Esc: всё отменяется (включая ожидание перезаписи).
            _overwritePath = string.Empty;
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
                        SaveFlowForPending(); // Сохранить
                        return;
                    case UnsavedAction.Discard:
                        ApplyPending(); // Не сохранять
                        return;
                    default:
                        _pending = PendingOp.None; // Отмена — только попап
                        SetMessage(_loc["msg.cancelled"]);
                        return;
                }
            case (ModalKind.Overwrite, 0):
                try
                {
                    _buf.Save(_overwritePath); // Да — перезаписать
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex)
                {
                    // Не сохранилось — отменяем всё, чтобы не потерять данные выходом.
                    _overwritePath = string.Empty;
                    _pending = PendingOp.None;
                    _modal = ModalState.Error(_loc, _loc["error.save"], DisplayError(ex));
                    return;
                }
                _overwritePath = string.Empty;
                ApplyPending();
                return;
            default:
                // About / Error / Нет — только закрыть (Нет отменяет и ожидание).
                _overwritePath = string.Empty;
                if (_pending != PendingOp.None)
                {
                    _pending = PendingOp.None;
                    SetMessage(_loc["msg.cancelled"]);
                }
                return;
        }
    }

    /// <summary>Действие кнопок попапа несохранённых изменений.</summary>
    internal enum UnsavedAction { Save, Discard, Cancel }

    /// <summary>Кнопка попапа → действие (0 — сохранить, 1 — не сохранять, прочее — отмена).</summary>
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
                        _modal = ModalState.Overwrite(_loc, Path.GetFileName(path));
                        return;
                    }
                    _buf.Save(path);
                    break;
                default:
                    _buf.Save();
                    break;
            }
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
            ApplyPending();
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _modal = ModalState.Error(_loc, _loc["error.save"], DisplayError(ex));
        }
    }

    private void ApplyPending()
    {
        PendingOp p = _pending;
        string path = _pendingPath;
        _pending = PendingOp.None;
        switch (p)
        {
            case PendingOp.Quit: _quitRequested = true; break;
            case PendingOp.New: ClearDoc(); break;
            case PendingOp.Open: LoadFile(path); break;
            default: break;
        }
    }

    /// <summary>Стартовый каталог менеджера: папка файла или текущая.</summary>
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

    /// <summary>
    /// Файловый менеджер модальным окном (свой цикл ввода, как Prompt).
    /// В Save-режиме перезапись подтверждается внутри (красный бокс).
    /// </summary>
    /// <returns>Выбранный путь или null (Esc).</returns>
    private string? RunPicker(PickerMode mode, string startDir, string initialName)
    {
        _picker = new FilePickerState(mode, startDir, initialName);
        try
        {
            while (true)
            {
                Render();
                InputEvent ev;
                try
                {
                    ev = _input.Read();
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
                if (ev is PasteInput paste)
                {
                    _picker.InsertName(paste.Text.Replace("\r", "").Replace("\n", ""));
                    continue;
                }
                var k = ((KeyInput)ev).Key;
                bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
                bool alt = (k.Modifiers & ConsoleModifiers.Alt) != 0;
                if (alt && (k.Modifiers & ConsoleModifiers.Control) == 0)
                {
                    // Навигация по каталогам (Backspace текст не трогает).
                    switch (k.Key)
                    {
                        case ConsoleKey.LeftArrow: _picker.UpDir(); break;
                        case ConsoleKey.RightArrow: _picker.EnterDir(); break;
                    }
                    continue;
                }
                if ((k.Modifiers & ConsoleModifiers.Control) != 0
                    && (k.Modifiers & ConsoleModifiers.Alt) == 0)
                {
                    // Ctrl в поле имени: по словам и удаление слов.
                    switch (k.Key)
                    {
                        case ConsoleKey.LeftArrow: _picker.MoveNameWord(-1, shift); break;
                        case ConsoleKey.RightArrow: _picker.MoveNameWord(1, shift); break;
                        case ConsoleKey.Backspace: _picker.DeleteNameWord(-1); break;
                        case ConsoleKey.Delete: _picker.DeleteNameWord(1); break;
                    }
                    continue; // прочий Ctrl в менеджере не используется
                }
                if ((k.Modifiers & ConsoleModifiers.Control) != 0)
                    continue; // Ctrl+Alt (AltGr) в менеджере не используется
                switch (k.Key)
                {
                    case ConsoleKey.Escape: return null;
                    case ConsoleKey.UpArrow: _picker.MoveHighlight(-1); break;
                    case ConsoleKey.DownArrow: _picker.MoveHighlight(1); break;
                    case ConsoleKey.PageUp: _picker.MoveHighlight(-10); break;
                    case ConsoleKey.PageDown: _picker.MoveHighlight(10); break;
                    case ConsoleKey.Home:
                        if (shift) _picker.HomeName(true);
                        else _picker.GotoFirst();
                        break;
                    case ConsoleKey.End:
                        if (shift) _picker.EndName(true);
                        else _picker.GotoLast();
                        break;
                    case ConsoleKey.Enter:
                        var (res, path) = _picker.Enter();
                        if (res == PickerEnterResult.Accepted && path is not null)
                            return path;
                        break;
                    case ConsoleKey.Backspace: _picker.Backspace(); break;
                    case ConsoleKey.Delete: _picker.DeleteChar(); break;
                    case ConsoleKey.LeftArrow: _picker.MoveNameCursor(-1, shift); break;
                    case ConsoleKey.RightArrow: _picker.MoveNameCursor(1, shift); break;
                    default:
                        if (!char.IsControl(k.KeyChar))
                            _picker.InsertName(k.KeyChar.ToString());
                        break;
                }
            }
        }
        finally
        {
            _picker = null;
        }
    }

    /// <summary>Путь для сохранения через менеджер (перезапись подтверждает модалка).</summary>
    private string? PickSavePath(string initialName) =>
        RunPicker(PickerMode.Save, StartDir(), initialName);

    private void Find()
    {
        string? term = Prompt(_loc["prompt.find"], _lastSearch);
        if (term is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (term.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        _lastSearch = term;
        FindNext();
    }

    private void FindNext() => JumpSearch(wrap: true, backward: false);

    private void FindPrev() => JumpSearch(wrap: true, backward: true);

    /// <summary>Прыжок к следующему/предыдущему вхождению со счётчиком «k/N».</summary>
    private void JumpSearch(bool wrap, bool backward)
    {
        if (string.IsNullOrEmpty(_lastSearch)) { Find(); return; }
        bool mc = _settings.SearchMatchCase, ww = _settings.SearchWholeWord;
        var hit = backward
            ? _buf.FindPrev(_lastSearch, _row, _col, mc, ww, wrap)
            : _buf.FindNext(_lastSearch, _row, _col + 1, mc, ww, wrap);
        if (hit is null) { SetMessage(_loc.Format("msg.search.miss", _lastSearch)); return; }
        (_row, _col) = (hit.Value.row, hit.Value.col);
        _sel.Clear(); // прыжок снимает выделение
        TrackCol();
        int total = _buf.CountMatches(_lastSearch, mc, ww);
        int idx = _buf.MatchOrdinal(_lastSearch, _row, _col, mc, ww);
        string key = hit.Value.wrapped ? "msg.search.wrap" : "msg.search.hit";
        SetMessage(_loc.Format(key, _lastSearch, idx, total));
    }

    /// <summary>
    /// Мгновенная замена по всему документу (как Replace All в MS Edit:
    /// одна undo-группа, курсор на месте). Два промпта — что и на что.
    /// </summary>
    private void Replace()
    {
        string? term = Prompt(_loc["prompt.replace.find"], _lastSearch);
        if (term is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (term.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        string? rep = Prompt(_loc["prompt.replace.with"], _lastReplace);
        if (rep is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        _lastSearch = term;
        _lastReplace = rep;
        int n = _buf.ReplaceAll(term, rep, 0, 0, _settings.SearchMatchCase, _settings.SearchWholeWord);
        ClampCursor();
        SetMessage(n > 0
            ? _loc.Format("msg.replace.done", n)
            : _loc.Format("msg.search.miss", term));
    }

    private void GoToLine()
    {
        string? s = Prompt(_loc.Format("prompt.goto", _buf.Count), string.Empty);
        if (s is null) return;
        if (int.TryParse(s.Trim(), out int n))
        {
            _row = Math.Clamp(n - 1, 0, _buf.Count - 1);
            _col = Math.Min(_col, _buf.GetLine(_row).Length);
            _sel.Clear(); // прыжок снимает выделение
            TrackCol();
        }
        else SetMessage(_loc["msg.notnumber"]);
    }

    /// <summary>Строки для построчных операций: охват выделения или текущая строка.</summary>
    private (int start, int end) LineBlock()
    {
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, _, er, _) = _sel.Normalize(_row, _col);
            return (sr, er);
        }
        return (_row, _row);
    }

    /// <summary>Дублирует строку/блок ниже, курсор — в копию на ту же относительную строку.</summary>
    private void DuplicateBlock()
    {
        var (s, e) = LineBlock();
        int copy = _buf.DuplicateLines(s, e);
        _row = copy + (_row - s);
        _sel.Clear();
        ClampCursor();
        TrackCol();
    }

    /// <summary>Двигает строку/блок на одну вверх (dir=-1) или вниз; на краю — тихо.</summary>
    private void MoveLineBlock(int dir)
    {
        var (s, e) = LineBlock();
        bool ok = dir < 0 ? _buf.MoveLinesUp(s, e) : _buf.MoveLinesDown(s, e);
        if (!ok) return;
        _row += dir; // блок сдвинулся целиком — курсор едет с ним
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
        _clipboard.Add(_buf.CutLine(_row));
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
        DeleteSelection(); // вставка поверх выделения
        _buf.PasteLines(_row, _col, _clipboard);
        if (_clipboard.Count == 1) _col += _clipboard[0].Length;
        else { _row += _clipboard.Count - 1; _col = _clipboard[^1].Length; }
        TrackCol();
    }

    private void SetMessage(string m)
    {
        _message = m;
        _messageUntil = DateTime.Now.AddSeconds(4);
    }

    private string CurrentMessage =>
        DateTime.Now <= _messageUntil ? _message : string.Empty;

    // ---------- Диалоги (в строке статуса) ----------

    private string? Prompt(string title, string initial)
    {
        var input = initial ?? string.Empty;
        int pos = input.Length;
        while (true)
        {
            DrawPrompt(title, input, pos);
            InputEvent ev;
            try
            {
                ev = _input.Read();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            if (ev is PasteInput paste)
            {
                // В однострочный промпт переводы строк не несём.
                string t = paste.Text.Replace("\r", "").Replace("\n", "");
                input = input.Insert(pos, t);
                pos += t.Length;
                continue;
            }
            var k = ((KeyInput)ev).Key;
            bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
            if (ctrl) continue;
            switch (k.Key)
            {
                case ConsoleKey.Escape: return null;
                case ConsoleKey.Enter: return input;
                case ConsoleKey.Backspace:
                    if (pos > 0) { input = input.Remove(pos - 1, 1); pos--; }
                    break;
                case ConsoleKey.Delete:
                    if (pos < input.Length) input = input.Remove(pos, 1);
                    break;
                case ConsoleKey.LeftArrow: if (pos > 0) pos--; break;
                case ConsoleKey.RightArrow: if (pos < input.Length) pos++; break;
                case ConsoleKey.Home: pos = 0; break;
                case ConsoleKey.End: pos = input.Length; break;
                default:
                    if (!char.IsControl(k.KeyChar))
                    {
                        input = input.Insert(pos, k.KeyChar.ToString());
                        pos++;
                    }
                    break;
            }
        }
    }

    private void DrawPrompt(string title, string input, int pos)
    {
        try
        {
            // Ресайз во время промпта — полный перерендер.
            if (Console.WindowWidth != _screen.Width || Console.WindowHeight != _screen.Height)
                Render();
        }
        catch { }
        int w = _screen.Width, h = _screen.Height;
        if (w < 10 || h < 4)
            return;
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

    // ---------- Отрисовка ----------

    private int TextHeight()
    {
        int h;
        try { h = Console.WindowHeight; } catch { return 10; }
        return Math.Max(1, h - 2);
    }

    private void EnsureVisible(int textHeight, int contentWidth)
    {
        ClampCursor();
        bool wrap = _settings.WordWrap;
        if (!wrap)
        {
            _topSeg = 0;
            if (_row < _top) _top = _row;
            if (_row >= _top + textHeight) _top = _row - textHeight + 1;
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
        int rows = CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, true, textHeight);
        if (rows < 0) // курсор выше видимого (сдвиг внутри длинной строки)
        {
            _top = _row; _topSeg = 0;
            rows = CursorVisualRow(_buf.Lines, _top, 0, _row, _col, contentWidth, true, textHeight);
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
            rows = CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, true, textHeight);
        }
        if (_top < 0) { _top = 0; _topSeg = 0; }
    }

    /// <summary>Индекс визуального сегмента для символьной колонки.</summary>
    internal static int CursorSeg(string line, int col, int contentWidth)
    {
        int vcol = TabStops.VisualWidth(line, col);
        return WordWrap.SegmentAt(WordWrap.SegmentStarts(line, contentWidth), vcol);
    }

    /// <summary>
    /// Визуальная строка курсора относительно (top, topSeg): сегменты строк
    /// [top, row) минус прокрученные плюс сегмент курсора. Чистая функция.
    /// Точность выше cap не гарантируется (для экрана достаточно cap=textHeight).
    /// </summary>
    internal static int CursorVisualRow(IReadOnlyList<string> lines, int top, int topSeg,
        int row, int col, int contentWidth, bool wrap, int cap = int.MaxValue)
    {
        if (!wrap) return row - top;
        int rows = -topSeg;
        for (int r = top; r < row && rows < cap; r++)
            rows += WordWrap.SegmentCount(lines[r], contentWidth);
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

        int textHeight = h - 2; // меню сверху, статус снизу
        bool wrap = _settings.WordWrap;
        int numWidth = Math.Max(4, _buf.Count.ToString().Length);
        int gutterWidth = _settings.ShowLineNumbers ? numWidth + 3 : 0; // "1234 │ " или нет
        int contentWidth = Math.Max(1, w - gutterWidth);

        EnsureVisible(textHeight, contentWidth);
        if (_screen.Width != w || _screen.Height != h)
            _screen.Resize(w, h);

        // Меню-бар (строка 0): File / Edit / Help + имя файла справа.
        DrawMenuBar(w);

        if (_helpOpen)
            DrawHelp(w, textHeight);
        else
            DrawText(w, textHeight, contentWidth, gutterWidth, numWidth, wrap);

        // Статусбар: слева позиция/сообщение, справа кодировка | EOL | отступ | файл.
        string msg = CurrentMessage;
        string pos = _loc.Format("status.pos", _row + 1, _buf.Count, _col + 1) + (_buf.IsModified ? " *" : "");
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            pos += " | " + _loc.Format("status.sel", SelectionLength(sr, sc, er, ec));
        }
        string left = string.IsNullOrEmpty(msg) ? $" {pos}" : $" {msg}";
        string file = _buf.FilePath is null ? _loc["status.noname"] : Path.GetFileName(_buf.FilePath);
        string right = $" {_buf.EncodingLabel} | {_buf.EndingLabel} | {_buf.IndentLabel} | {file} ";
        _screen.Text(0, h - 1, StatusBar.Build(left, right, w), _theme.StatusFg, _theme.StatusBg);

        // Поверх текста: раскрытое меню, менеджер, модальный попап и настройки.
        DrawDropdown(w, h);
        DrawPicker(w, h);
        DrawModal(w, h);
        if (_settingsOpen && _settingsDlg is not null)
            DrawSettings(_settingsDlg);

        // Один diff-вывод за кадр — без мигания.
        _screen.Flush();

        // Аппаратный курсор ставим один раз за кадр:
        // менеджер — в поле имени, иначе текст (прячем под меню, попапом, настройками, справкой).
        bool uiOpen = _menu is not null || _modal is not null || _settingsOpen || _helpOpen;
        string curLine = _buf.GetLine(_row);
        int vcolCur = TabStops.VisualWidth(curLine, _col);
        int curBase = wrap
            ? WordWrap.SegmentStarts(curLine, contentWidth)[CursorSeg(curLine, _col, contentWidth)]
            : _left;
        int cx = gutterWidth + (vcolCur - curBase);
        int cy = 1 + CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, wrap, textHeight);
        bool pickerCursor = _picker is not null && _modal is null && !_settingsOpen && _pickerCursorX >= 0;
        bool placed = pickerCursor
            || (!uiOpen && _picker is null && cy >= 1 && cy < 1 + textHeight && cx >= gutterWidth && cx < w);
        try
        {
            if (placed)
                Console.SetCursorPosition(pickerCursor ? _pickerCursorX : cx, pickerCursor ? _pickerCursorY : cy);
            Console.CursorVisible = placed;
        }
        catch { }
    }

    /// <summary>
    /// Текст посимвольно в Screen — цвет ячейки зависит от
    /// выделения / совпадения поиска / текущей строки.
    /// Без wrap — один экранный ряд на строку; с wrap — по сегменту
    /// (номер только на первом, дальше пустой гуттер).
    /// </summary>
    private void DrawText(int w, int textHeight, int contentWidth, int gutterWidth, int numWidth, bool wrap)
    {
        int y = 1;
        int fileLine = _top;
        int firstSeg = _topSeg;
        while (y < 1 + textHeight && fileLine < _buf.Count)
        {
            string line = _buf.GetLine(fileLine);
            bool isCur = fileLine == _row;
            GetRowSelection(fileLine, line.Length, out int selA, out int selB);
            List<int> starts = wrap ? WordWrap.SegmentStarts(line, contentWidth) : new List<int> { 0 };
            for (int s = firstSeg; s < starts.Count && y < 1 + textHeight; s++)
            {
                int segStart = starts[s];
                int segEnd = s + 1 < starts.Count ? starts[s + 1] : int.MaxValue;
                int @base = wrap ? segStart : _left;
                if (gutterWidth > 0)
                    _screen.Text(0, y, s == 0
                        ? $"{(fileLine + 1).ToString().PadLeft(numWidth)} │ "
                        : $"{new string(' ', numWidth)} │ ", _theme.GutterFg, _theme.EditorBg);
                bool[] isMatch = FindMatches(TabStops.Slice(line, @base, contentWidth),
                    _lastSearch, _settings.SearchMatchCase, _settings.SearchWholeWord);
                int vpos = 0; // визуальная позиция в строке
                int ci = 0;   // индекс символа
                foreach (char c in line)
                {
                    int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
                    if (vpos + cw <= segStart) { vpos += cw; ci++; continue; }
                    if (vpos >= segEnd) break;
                    for (int k = 0; k < cw; k++)
                    {
                        int vc = vpos + k - @base; // колонка вьюпорта/сегмента
                        if (vc < 0)
                            continue;
                        if (vc >= contentWidth)
                            break;
                        bool sel = ci >= selA && ci < selB;
                        bool match = vc < isMatch.Length && isMatch[vc];
                        (Rgb fg, Rgb bg) = (sel, match, isCur) switch
                        {
                            (true, _, _) => (_theme.SelFg, _theme.SelBg),
                            (_, true, _) => (_theme.MatchFg, _theme.MatchBg),
                            (_, _, true) => (_theme.CurLineFg, _theme.CurLineBg),
                            _ => (_theme.EditorFg, _theme.EditorBg),
                        };
                        _screen.Set(gutterWidth + vc, y, c == '\t' ? ' ' : c, fg, bg);
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
                _screen.Fill(gutterWidth + filled, y, contentWidth - filled, ' ', tailFg, tailBg);
                y++;
            }
            fileLine++;
            firstSeg = 0;
        }
        while (y < 1 + textHeight)
        {
            _screen.Text(0, y, ("~".PadRight(w))[..w], _theme.FillerFg, _theme.EditorBg);
            y++;
        }
    }

    /// <summary>Границы выделения в символах для строки (a==b — нет выделения).</summary>
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

    /// <summary>Маска совпадений поиска по раскрытой строке (с учётом опций).</summary>
    private static bool[] FindMatches(string expanded, string term, bool matchCase, bool wholeWord)
    {
        var m = new bool[expanded.Length];
        if (string.IsNullOrEmpty(term))
            return m;
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

    /// <summary>Границы слова в раскрытой строке (для подсветки).</summary>
    private static bool IsWhole(string s, int idx, int len) =>
        (idx == 0 || !(char.IsLetterOrDigit(s[idx - 1]) || s[idx - 1] == '_')) &&
        (idx + len >= s.Length || !(char.IsLetterOrDigit(s[idx + len]) || s[idx + len] == '_'));

    /// <summary>
    /// Меню-бар в строке 0 (как menubar в MS Edit): меню слева, имя файла справа.
    /// Запоминает x-координаты меню для дропдауна.
    /// </summary>
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
                // Раскрытое меню — подсветка, как в MS Edit.
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
            case (<= 0, _):
                break; // Места нет — имя файла не показываем.
            case (_, true):
                _screen.Text(x, 0, right[..rest], _theme.MenuFg, _theme.MenuBarBg);
                break;
            default:
                _screen.Text(x, 0, new string(' ', rest - right.Length) + right, _theme.MenuFg, _theme.MenuBarBg);
                break;
        }
    }

    /// <summary>Ячейка меню с жёлтым хоткеем (аналог underline-акселератора в MS Edit).</summary>
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

    /// <summary>
    /// Раскрытое меню под баром: рамка, пункты, шорткаты справа
    /// (аналог flyout-блока в menubar_menu_begin из MS Edit).
    /// </summary>
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
        int maxRows = h - 1 - y; // не залезать на статусбар
        if (maxRows < 3)
            return;
        int rows = Math.Min(m.Items.Count, maxRows - 2);

        Rgb borderFg = _theme.DropBorderFg;
        Rgb borderBg = _theme.DropBg;
        _screen.Text(x, y, "┌" + new string('─', boxW - 2) + "┐", borderFg, borderBg);
        for (int i = 0; i < rows; i++)
        {
            MenuItem it = m.Items[i];
            bool sel = i == _menu.SelectedIndex;
            // Выбранный пункт — зелёный, как в MS Edit.
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

    /// <summary>Диалог настроек своим циклом ввода (как менеджер; рисуется внутри Render).</summary>
    private void RunSettings()
    {
        var dlg = new SettingsDialogState();
        _settingsOpen = true;
        _settingsDlg = dlg;
        try
        {
            while (true)
            {
                Render(); // один flush за кадр вместе с диалогом — без мигания
                InputEvent ev;
                try
                {
                    ev = _input.Read();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                if (ev is not KeyInput key)
                    continue; // вставка в настройках не нужна
                var k = key.Key;
                if ((k.Modifiers & ConsoleModifiers.Control) != 0)
                    continue;
                switch (k.Key)
                {
                    case ConsoleKey.Escape:
                    case ConsoleKey.Enter:
                        return; // изменения сохраняются сразу при листании
                    case ConsoleKey.UpArrow: dlg.Move(-1); break;
                    case ConsoleKey.DownArrow: dlg.Move(1); break;
                    case ConsoleKey.Home: dlg.Move(-SettingsDialogState.RowCount); break;
                    case ConsoleKey.End: dlg.Move(SettingsDialogState.RowCount); break;
                    case ConsoleKey.LeftArrow: CycleSetting(dlg, -1); break;
                    case ConsoleKey.RightArrow: CycleSetting(dlg, 1); break;
                }
            }
        }
        finally
        {
            _settingsOpen = false;
            _settingsDlg = null;
        }
    }

    /// <summary>Листание значения настройки с применением и сохранением (тогглы — переворот).</summary>
    private void CycleSetting(SettingsDialogState dlg, int dir)
    {
        switch (dlg.Row)
        {
            case 0:
                int ti = SettingsDialogState.Cycle(
                    Array.IndexOf(Themes.Names, _settings.Theme), Themes.Names.Length, dir);
                _settings.Theme = Themes.Names[ti];
                break;
            case 1:
                int li = SettingsDialogState.Cycle(
                    Array.IndexOf(Loc.Supported, _settings.Language), Loc.Supported.Length, dir);
                _settings.Language = Loc.Supported[li];
                break;
            case 2:
                _settings.SearchMatchCase = !_settings.SearchMatchCase;
                break;
            case 3:
                _settings.SearchWholeWord = !_settings.SearchWholeWord;
                break;
            case 4:
                _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
                break;
            default:
                _settings.WordWrap = !_settings.WordWrap;
                break;
        }
        _store.Save(_settings);
        ApplySettings();
    }

    private string SettingsThemeName() => _settings.Theme == "light" ? _loc["settings.light"] : _loc["settings.dark"];

    private string OnOff(bool v) => v ? _loc["settings.on"] : _loc["settings.off"];

    private static string SettingsLangName(string lang) => lang == "en" ? "English" : "Русский";

    /// <summary>Отрисовка диалога настроек поверх всего (без хинта).</summary>
    private void DrawSettings(SettingsDialogState dlg)
    {
        int w = _screen.Width, h = _screen.Height;
        if (w < 20 || h < 5)
            return;
        string title = _loc["settings.title"];
        string[] labels = [_loc["settings.theme"], _loc["settings.lang"],
            _loc["settings.matchcase"], _loc["settings.wholeword"],
            _loc["settings.shownumbers"], _loc["settings.wordwrap"]];
        string langName = SettingsLangName(_loc.Language);
        string[] values = [SettingsThemeName(), langName,
            OnOff(_settings.SearchMatchCase), OnOff(_settings.SearchWholeWord),
            OnOff(_settings.ShowLineNumbers), OnOff(_settings.WordWrap)];
        int inner = 0;
        for (int i = 0; i < SettingsDialogState.RowCount; i++)
            inner = Math.Max(inner, labels[i].Length + values[i].Length + 8);
        int boxW = Math.Min(Math.Max(inner + 2, title.Length + 6), w);
        inner = boxW - 2;
        int boxH = SettingsDialogState.RowCount + 2; // заголовок + строки + низ
        int x0 = Math.Max(0, (w - boxW) / 2);
        int y0 = Math.Max(0, (h - boxH) / 2);
        if (y0 + boxH > h)
            return;
        Theme t = _theme;
        _screen.Text(x0, y0, Screen.TitleRow(title, boxW), t.ModalFg, t.ModalBg);
        for (int i = 0; i < SettingsDialogState.RowCount; i++)
        {
            string cell = $" {labels[i]}: < {values[i]} >";
            if (cell.Length > inner)
                cell = cell[..inner];
            if (i == dlg.Row)
                _screen.Text(x0, y0 + 1 + i, "│" + cell.PadRight(inner) + "│", t.ButtonSelFg, t.ButtonSelBg);
            else
                _screen.Text(x0, y0 + 1 + i, "│" + cell.PadRight(inner) + "│", t.ModalFg, t.ModalBg);
        }
        _screen.Text(x0, y0 + 1 + SettingsDialogState.RowCount,
            "└" + new string('─', inner) + "┘", t.ModalFg, t.ModalBg);
    }

    /// <summary>Клавиша на экране справки: Esc/F1/Enter — закрыть, остальное — скролл/игнор.</summary>
    private void HandleHelpKey(ConsoleKeyInfo k)
    {
        if ((k.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
            return; // системные комбинации в справке не работают
        switch (k.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F1:
            case ConsoleKey.Enter:
                _helpOpen = false;
                break;
            case ConsoleKey.UpArrow: _helpScroll--; break;
            case ConsoleKey.DownArrow: _helpScroll++; break;
            case ConsoleKey.Home: _helpScroll = 0; break;
            case ConsoleKey.End: _helpScroll = int.MaxValue; break;
            case ConsoleKey.PageUp: _helpScroll -= Math.Max(1, TextHeight()); break;
            case ConsoleKey.PageDown: _helpScroll += Math.Max(1, TextHeight()); break;
        }
    }

    /// <summary>Строки справки (те же ключи, что в --help).</summary>
    private List<string> HelpLines() => new()
    {
        _loc["help.title"],
        string.Empty,
        _loc["help.usage"],
        _loc["help.usage.line"],
        string.Empty,
        _loc["help.keys"],
        _loc["help.k1"],
        _loc["help.k2"],
        _loc["help.k3"],
        _loc["help.k4"],
        _loc["help.k5"],
        _loc["help.k6"],
        _loc["help.k7"],
        _loc["help.k8"],
        _loc["help.k9"],
        _loc["help.k10"],
        _loc["help.k11"],
        _loc["help.k12"],
        string.Empty,
        _loc["help.status"],
        _loc.Format("help.config", _store.Path),
        string.Empty,
        _loc["help.note1"],
        _loc["help.note2"],
    };

    /// <summary>Справка поверх текстовой области (меню-бар и статусбар свои).</summary>
    private void DrawHelp(int w, int textHeight)
    {
        List<string> lines = HelpLines();
        _helpScroll = Math.Clamp(_helpScroll, 0, Math.Max(0, lines.Count - textHeight));
        for (int i = 0; i < textHeight; i++)
        {
            int y = 1 + i;
            string s = _helpScroll + i < lines.Count ? lines[_helpScroll + i] : string.Empty;
            if (s.Length > w) s = s[..w];
            bool title = _helpScroll + i == 0;
            _screen.Text(0, y, s.PadRight(w)[..w],
                title ? _theme.MenuOpenFg : _theme.EditorFg,
                title ? _theme.MenuOpenBg : _theme.EditorBg);
        }
    }

    /// <summary>
    /// Файловый менеджер большой модалкой (как file-picker в MS Edit):
    /// путь, поле имени, список [.., папки/, файлы], хинт.
    /// </summary>
    private void DrawPicker(int w, int h)
    {
        if (_picker is null)
        {
            _pickerCursorX = -1;
            return;
        }
        FilePickerState p = _picker;
        int bw = Math.Min(Math.Max(w - 10, 30), w);
        int bh = Math.Min(Math.Max(h - 8, 14), h);
        int x0 = Math.Max(0, (w - bw) / 2);
        int y0 = Math.Max(0, (h - bh) / 2);
        Rgb bg = _theme.ModalBg;
        Rgb fg = _theme.ModalFg;
        int inner = bw - 2;

        string title = p.Mode == PickerMode.Open ? _loc["picker.open.title"] : _loc["picker.save.title"];
        _screen.Text(x0, y0, Screen.TitleRow(title, bw), fg, bg);

        string dirLabel = p.CurrentDir == "" ? _loc["picker.drives"] : p.CurrentDir;
        string dirRow = _loc["picker.dir"] + MiddleTruncate(dirLabel, Math.Max(0, inner - _loc["picker.dir"].Length));
        _screen.Text(x0, y0 + 1, "│" + dirRow.PadRight(inner)[..inner] + "│", fg, bg);

        // Поле имени (хвост + курсор, как в промпте; выделение — инверсией).
        string nameTag = _loc["picker.name"];
        string full = nameTag + p.Name;
        int shift = Math.Max(0, full.Length - inner);
        p.GetNameSelection(out int selA, out int selB);
        for (int i = 0; i < inner; i++)
        {
            int fi = shift + i; // индекс в full
            char ch = fi < full.Length ? full[fi] : ' ';
            bool sel = false;
            if (fi >= nameTag.Length)
            {
                int ni = fi - nameTag.Length; // индекс в Name
                sel = ni >= selA && ni < selB;
            }
            _screen.Set(x0 + 1 + i, y0 + 2, ch, sel ? _theme.SelFg : fg, sel ? _theme.SelBg : bg);
        }
        _screen.Text(x0, y0 + 2, "│", fg, bg);
        _screen.Text(x0 + bw - 1, y0 + 2, "│", fg, bg);
        int ncx = x0 + 1 + nameTag.Length + p.NamePos - shift;
        _pickerCursorX = ncx >= x0 + 1 && ncx < x0 + bw - 1 ? ncx : -1;
        _pickerCursorY = y0 + 2;

        // Список с прокруткой: строки y0+3 .. y0+bh-3, хинт, низ.
        int listRows = bh - 5;
        p.EnsureVisible(Math.Max(1, listRows));
        for (int i = 0; i < listRows; i++)
        {
            int yy = y0 + 3 + i;
            if (i < p.Entries.Count - p.Top)
            {
                PickerEntry e = p.Entries[p.Top + i];
                bool sel = p.Top + i == p.Selected;
                (Rgb efg, Rgb ebg) = sel
                    ? (_theme.DropSelFg, _theme.DropSelBg)
                    : e.IsDir
                        ? (e.Name == ".." ? _theme.PickerUpFg : _theme.PickerDirFg, bg)
                        : (_theme.PickerFileFg, bg);
                string label = e.DisplayName;
                if (label.Length > inner)
                    label = label[..Math.Max(0, inner)];
                _screen.Text(x0, yy, "│", fg, bg);
                _screen.Text(x0 + 1, yy, label.PadRight(inner)[..inner], efg, ebg);
                _screen.Text(x0 + bw - 1, yy, "│", fg, bg);
            }
            else
            {
                string empty = p.Error == "BadPath" ? _loc["picker.badpath"]
                    : p.Error ?? (p.Entries.Count == 0 ? _loc["picker.empty"] : "");
                Rgb efg = p.Error is null ? _theme.PickerEmptyFg : _theme.PickerErrorFg;
                _screen.Text(x0, yy, "│" + empty.PadRight(inner)[..inner] + "│", efg, bg);
            }
        }

        string hint = _loc["picker.hint"];
        _screen.Text(x0, y0 + bh - 2, "│" + CenterPad(hint, inner)[..inner] + "│", _theme.PickerHintFg, bg);
        _screen.Text(x0, y0 + bh - 1, "└" + new string('─', inner) + "┘", fg, bg);
    }

    private static string MiddleTruncate(string s, int width)
    {
        if (s.Length <= width)
            return s;
        if (width <= 6)
            return s[..Math.Max(0, width)];
        int head = (width - 3) / 2;
        return s[..head] + "..." + s[^(width - 3 - head)..];
    }

    /// <summary>
    /// Центрированный модальный попап с рамкой и заголовком
    /// (аналог modal_begin/modal_end в MS Edit).
    /// </summary>
    private void DrawModal(int w, int h)
    {
        if (_modal is null)
            return;
        ModalState m = _modal;

        Rgb bg = m.Danger ? _theme.ModalDangerBg : _theme.ModalBg;
        Rgb fg = m.Danger ? _theme.ModalDangerFg : _theme.ModalFg;

        int btnWidth = m.Buttons.Sum(b => b.Label.Length + 4) + (m.Buttons.Count - 1) * 2;
        int content = m.Title.Length + 2;
        foreach (string line in m.Lines)
            content = Math.Max(content, line.Length);
        content = Math.Max(content, btnWidth);
        content = Math.Max(content, m.Hint.Length);
        int boxW = Math.Min(Math.Max(content + 6, 24), w);
        if (boxW < 12)
            return;
        int boxH = m.Lines.Count + (m.Hint.Length > 0 ? 5 : 4); // верх, строки, пусто, кнопки, [хинт,] низ
        int x0 = Math.Max(0, (w - boxW) / 2);
        int y0 = Math.Max(0, (h - boxH) / 2);
        if (y0 + boxH > h)
            return;

        // Верх с заголовком: ┌─ Title ───┐
        _screen.Text(x0, y0, Screen.TitleRow(m.Title, boxW), fg, bg);
        // Строки текста по центру.
        for (int i = 0; i < m.Lines.Count; i++)
            _screen.Text(x0, y0 + 1 + i, "│" + CenterPad(m.Lines[i], boxW - 2) + "│", fg, bg);
        // Кнопки по центру (хоткеи уже в названиях, напр. [Y]).
        _screen.Text(x0, y0 + 1 + m.Lines.Count, "│" + new string(' ', boxW - 2) + "│", fg, bg);
        int used = 0;
        var cells = new List<string>();
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            string cell = m.Buttons[i].Label;
            cells.Add(cell);
            used += cell.Length + 4;
        }
        used -= 4;
        int padLeft = Math.Max(2, (boxW - 2 - used) / 2);
        var btnRow = new System.Text.StringBuilder();
        btnRow.Append('│');
        btnRow.Append(' ', padLeft);
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            if (i > 0)
                btnRow.Append("    ");
            btnRow.Append(cells[i]);
        }
        int rest = boxW - 2 - padLeft - used;
        if (rest > 0)
            btnRow.Append(' ', rest);
        btnRow.Append('│');
        int btnY = y0 + 2 + m.Lines.Count;
        _screen.Text(x0, btnY, btnRow.ToString(), fg, bg);
        // Подсветка выбранной кнопки поверх.
        int bx = x0 + 1 + padLeft;
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            if (i == m.Selected)
                _screen.Text(bx, btnY, cells[i], _theme.ButtonSelFg, _theme.ButtonSelBg);
            bx += cells[i].Length + 4;
        }
        // Хинт (если есть) и низ.
        int bottomY = btnY + 1;
        if (m.Hint.Length > 0)
        {
            Rgb hintFg = m.Danger ? _theme.ModalHintDangerFg : _theme.ModalHintFg;
            _screen.Text(x0, bottomY, "│" + CenterPad(m.Hint, boxW - 2) + "│", hintFg, bg);
            bottomY++;
        }
        _screen.Text(x0, bottomY, "└" + new string('─', boxW - 2) + "┘", fg, bg);
    }

    private static string CenterPad(string s, int width)
    {
        if (s.Length >= width)
            return s[..Math.Max(0, width)];
        int left = (width - s.Length) / 2;
        return new string(' ', left) + s + new string(' ', width - s.Length - left);
    }

}
