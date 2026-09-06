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
    private string _message = string.Empty;
    private DateTime _messageUntil = DateTime.MinValue;
    private readonly List<string> _clipboard = new();
    private string _lastSearch = string.Empty;
    private bool _quitRequested;
    private readonly TextSelection _sel = new();
    private MenuState? _menu;   // null — меню-бар закрыт
    private ModalState? _modal; // null — попапа нет
    private PendingOp _pending = PendingOp.None;
    private string _pendingPath = string.Empty;
    private readonly List<int> _menuX = new(); // x-координаты меню в баре (из рендера)
    private readonly Screen _screen = new(); // кадр + diff-вывод (без мигания)
    private readonly InputReader _input = new();

    private const string AppVersion = "0.1.0";

    /// <summary>Отложенное действие после диалога «несохранённые изменения».</summary>
    private enum PendingOp { None, Quit, New, Open }

    public TuiEditor(TextBuffer buf)
    {
        _buf = buf;
    }

    public void Run()
    {
        Console.TreatControlCAsInput = true;
        Console.CursorVisible = false;

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
            case EditorCommand.About: _modal = ModalState.About(AppVersion); return;
            case EditorCommand.Find: Find(); return;
            case EditorCommand.FindNext: FindNext(); return;
            case EditorCommand.GoToLine: GoToLine(); return;
            case EditorCommand.CutLine: CutLine(); return;
            case EditorCommand.CopyLine: CopyLine(); return;
            case EditorCommand.Paste: Paste(); return;
            case EditorCommand.SaveAs: SaveAs(); return;
            case EditorCommand.Undo: _buf.Undo(); _sel.Clear(); ClampCursor(); SetMessage("Отмена"); return;
            case EditorCommand.Redo: _buf.Redo(); _sel.Clear(); ClampCursor(); SetMessage("Возврат"); return;
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
        SetMessage($"Выделено: {SelectionLength(0, 0, _row, _col)} зн.");
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
                    SetMessage($"Сохранено: {_buf.FilePath}");
                }
                catch (Exception ex) { SetMessage($"Ошибка сохранения: {ex.Message}"); }
                return;
        }
    }

    private void SaveAs()
    {
        string? path = Prompt("Сохранить как: ", _buf.FilePath ?? string.Empty);
        if (path is null) { SetMessage("Отменено"); return; }
        if (string.IsNullOrWhiteSpace(path)) { SetMessage("Пустое имя файла"); return; }
        try
        {
            _buf.Save(path.Trim());
            SetMessage($"Сохранено: {_buf.FilePath}");
        }
        catch (Exception ex) { SetMessage($"Ошибка сохранения: {ex.Message}"); }
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
        _modal = ModalState.UnsavedQuit();
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
        _modal = ModalState.UnsavedQuit();
    }

    /// <summary>Открыть файл: запрос пути, затем диалог при несохранённых изменениях.</summary>
    private void DoOpen()
    {
        string? path = Prompt("Открыть: ", string.Empty);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetMessage("Отменено");
            return;
        }
        path = path.Trim();
        if (!_buf.IsModified)
        {
            LoadFile(path);
            return;
        }
        _pending = PendingOp.Open;
        _pendingPath = path;
        _modal = ModalState.UnsavedQuit();
    }

    private void ClearDoc()
    {
        _buf.Clear();
        ResetCursor();
        SetMessage("Новый документ");
    }

    private void ResetCursor()
    {
        _row = _col = _desiredCol = _top = _left = 0;
    }

    private void LoadFile(string path)
    {
        bool existed = File.Exists(path);
        try
        {
            _buf.Open(path);
            ResetCursor();
            SetMessage(existed ? $"Открыто: {path}" : $"Новый файл: {path}");
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _modal = ModalState.Error("Ошибка открытия", ex.Message);
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
        _menu = new MenuState(BuildMenus());
        _menu.Open(index);
    }

    /// <summary>
    /// Меню-бар в стиле MS Edit: File / Edit / Help.
    /// Состав пунктов повторяет draw_menubar.rs (New/Open/Save/SaveAs/Exit и т.д.).
    /// </summary>
    private static List<TopMenu> BuildMenus() => new()
    {
        new TopMenu("File", 'F', new List<MenuItem>
        {
            new("Новый", 'N', null, EditorCommand.NewFile),
            new("Открыть...", 'O', null, EditorCommand.OpenFile),
            new("Сохранить", 'S', "F2", EditorCommand.Save),
            new("Сохранить как...", 'A', "^O", EditorCommand.SaveAs),
            new("Выход", 'X', "^Q", EditorCommand.Quit),
        }),
        new TopMenu("Edit", 'E', new List<MenuItem>
        {
            new("Отмена", 'U', "^Z", EditorCommand.Undo),
            new("Возврат", 'R', "^Y", EditorCommand.Redo),
            new("Вырезать строку", 'T', "^K", EditorCommand.CutLine),
            new("Копировать строку", 'C', "^C", EditorCommand.CopyLine),
            new("Вставить", 'P', "^U", EditorCommand.Paste),
            new("Найти...", 'F', "^F", EditorCommand.Find),
            new("Строка...", 'G', "^G", EditorCommand.GoToLine),
            new("Выделить всё", 'A', "^A", EditorCommand.SelectAll),
        }),
        new TopMenu("Help", 'H', new List<MenuItem>
        {
            new("О программе", 'A', null, EditorCommand.About),
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
            // Esc: About/Error просто закрываются, ожидание действия отменяется.
            if (_pending != PendingOp.None)
            {
                _pending = PendingOp.None;
                SetMessage("Отменено");
            }
            return;
        }
        switch (m.Kind, o.Button)
        {
            case (ModalKind.UnsavedQuit, 0):
                SaveFlowForPending(); // Сохранить
                return;
            case (ModalKind.UnsavedQuit, _):
                ApplyPending(); // Не сохранять
                return;
            default:
                _pending = PendingOp.None; // About / Error: любая кнопка закрывает
                return;
        }
    }

    /// <summary>Сохранение из попапа с последующим отложенным действием.</summary>
    private void SaveFlowForPending()
    {
        try
        {
            switch (_buf.FilePath)
            {
                case null:
                    string? path = Prompt("Сохранить как: ", string.Empty);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        _pending = PendingOp.None;
                        SetMessage("Отменено");
                        return;
                    }
                    _buf.Save(path.Trim());
                    break;
                default:
                    _buf.Save();
                    break;
            }
            SetMessage($"Сохранено: {_buf.FilePath}");
            ApplyPending();
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _modal = ModalState.Error("Ошибка сохранения", ex.Message);
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

    private void Find()
    {
        string? term = Prompt("Найти: ", _lastSearch);
        if (term is null) { SetMessage("Поиск отменён"); return; }
        if (term.Length == 0) { SetMessage("Пустой запрос"); return; }
        _lastSearch = term;
        FindNext();
    }

    private void FindNext()
    {
        if (string.IsNullOrEmpty(_lastSearch)) { Find(); return; }
        var hit = _buf.FindNext(_lastSearch, _row, _col + 1);
        if (hit is null) SetMessage($"«{_lastSearch}» не найдено");
        else
        {
            (_row, _col) = hit.Value;
            _sel.Clear(); // прыжок снимает выделение
            TrackCol();
            SetMessage($"Найдено: «{_lastSearch}» (F3 — далее)");
        }
    }

    private void GoToLine()
    {
        string? s = Prompt($"Номер строки (1-{_buf.Count}): ", string.Empty);
        if (s is null) return;
        if (int.TryParse(s.Trim(), out int n))
        {
            _row = Math.Clamp(n - 1, 0, _buf.Count - 1);
            _col = Math.Min(_col, _buf.GetLine(_row).Length);
            _sel.Clear(); // прыжок снимает выделение
            TrackCol();
        }
        else SetMessage("Не число");
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
            SetMessage($"Вырезано: {n} зн. (^U — вставить)");
            return;
        }
        _clipboard.Add(_buf.CutLine(_row));
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        TrackCol();
        SetMessage("Строка вырезана (^U — вставить)");
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
            SetMessage($"Скопировано: {SelectionLength(sr, sc, er, ec)} зн.");
            return;
        }
        _clipboard.Add(_buf.GetLine(_row));
        SystemClipboard.TryExport(_clipboard);
        SetMessage("Строка скопирована");
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) { SetMessage("Буфер пуст"); return; }
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
        _screen.Text(0, row, text.PadRight(w)[..w], ConsoleColor.White, ConsoleColor.DarkBlue);
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
        if (_row < _top) _top = _row;
        if (_row >= _top + textHeight) _top = _row - textHeight + 1;
        int vcol = TabStops.VisualWidth(_buf.GetLine(_row), _col);
        if (vcol < _left) _left = vcol;
        if (vcol >= _left + contentWidth) _left = vcol - contentWidth + 1;
        if (_top < 0) _top = 0;
        if (_left < 0) _left = 0;
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
                Console.WriteLine("Окно слишком маленькое для редактора.");
            }
            catch { }
            return;
        }

        int textHeight = h - 2; // меню сверху, статус снизу
        int numWidth = Math.Max(4, _buf.Count.ToString().Length);
        int gutterWidth = numWidth + 3; // "1234 │ "
        int contentWidth = Math.Max(1, w - gutterWidth);

        EnsureVisible(textHeight, contentWidth);
        if (_screen.Width != w || _screen.Height != h)
            _screen.Resize(w, h);

        // Меню-бар (строка 0): File / Edit / Help + имя файла справа.
        DrawMenuBar(w);

        // Текст: посимвольно в Screen — цвет ячейки зависит от
        // выделения / совпадения поиска / текущей строки.
        for (int i = 0; i < textHeight; i++)
        {
            int fileLine = _top + i;
            int y = 1 + i;
            if (fileLine >= _buf.Count)
            {
                _screen.Text(0, y, ("~".PadRight(w))[..w], ConsoleColor.DarkBlue, ConsoleColor.Black);
                continue;
            }
            _screen.Text(0, y, $"{(fileLine + 1).ToString().PadLeft(numWidth)} │ ", ConsoleColor.DarkGray, ConsoleColor.Black);

            string line = _buf.GetLine(fileLine);
            bool isCur = fileLine == _row;
            bool[] isMatch = FindMatches(TabStops.Slice(line, _left, contentWidth), _lastSearch);
            GetRowSelection(fileLine, line.Length, out int selA, out int selB);

            int vpos = 0; // визуальная позиция в строке
            int ci = 0;   // индекс символа
            foreach (char c in line)
            {
                int cw = c == '\t' ? TabStops.Width - vpos % TabStops.Width : 1;
                for (int k = 0; k < cw; k++)
                {
                    int vc = vpos + k - _left; // колонка вьюпорта
                    if (vc < 0)
                        continue;
                    if (vc >= contentWidth)
                        break;
                    bool sel = ci >= selA && ci < selB;
                    bool match = vc < isMatch.Length && isMatch[vc];
                    (ConsoleColor fg, ConsoleColor bg) = (sel, match, isCur) switch
                    {
                        (true, _, _) => (ConsoleColor.White, ConsoleColor.DarkBlue),
                        (_, true, _) => (ConsoleColor.Black, ConsoleColor.DarkYellow),
                        (_, _, true) => (ConsoleColor.White, ConsoleColor.DarkGray),
                        _ => (ConsoleColor.Gray, ConsoleColor.Black),
                    };
                    _screen.Set(gutterWidth + vc, y, c == '\t' ? ' ' : c, fg, bg);
                }
                vpos += cw;
                if (vpos - _left >= contentWidth)
                    break;
                ci++;
            }
            // Хвост строки — пробелы обычным цветом.
            int filled = Math.Clamp(vpos - _left, 0, contentWidth);
            (ConsoleColor tailFg, ConsoleColor tailBg) = isCur
                ? (ConsoleColor.White, ConsoleColor.DarkGray)
                : (ConsoleColor.Gray, ConsoleColor.Black);
            _screen.Fill(gutterWidth + filled, y, contentWidth - filled, ' ', tailFg, tailBg);
        }

        // Статусбар: слева позиция/сообщение, справа кодировка | EOL | отступ | файл.
        string msg = CurrentMessage;
        string pos = $"Стр {_row + 1}/{_buf.Count} Стлб {_col + 1}{(_buf.IsModified ? " *" : "")}";
        if (_sel.HasSelection(_row, _col))
        {
            var (sr, sc, er, ec) = _sel.Normalize(_row, _col);
            pos += $" | Выделено {SelectionLength(sr, sc, er, ec)}";
        }
        string left = string.IsNullOrEmpty(msg) ? $" {pos}" : $" {msg}";
        string file = _buf.FilePath is null ? "без имени" : Path.GetFileName(_buf.FilePath);
        string right = $" {_buf.EncodingLabel} | {_buf.EndingLabel} | {_buf.IndentLabel} | {file} ";
        _screen.Text(0, h - 1, StatusBar.Build(left, right, w), ConsoleColor.Black, ConsoleColor.Gray);

        // Поверх текста: раскрытое меню и модальный попап.
        DrawDropdown(w, h);
        DrawModal(w, h);

        // Один diff-вывод за кадр — без мигания.
        _screen.Flush();

        // Аппаратный курсор ставим один раз за кадр (прячем под меню и попапом).
        bool uiOpen = _menu is not null || _modal is not null;
        int cx = gutterWidth + (TabStops.VisualWidth(_buf.GetLine(_row), _col) - _left);
        int cy = 1 + (_row - _top);
        bool placed = !uiOpen && cy >= 1 && cy < 1 + textHeight && cx >= gutterWidth && cx < w;
        try
        {
            if (placed)
                Console.SetCursorPosition(cx, cy);
            Console.CursorVisible = placed;
        }
        catch { }
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

    /// <summary>Маска совпадений поиска по раскрытой строке.</summary>
    private static bool[] FindMatches(string expanded, string term)
    {
        var m = new bool[expanded.Length];
        if (string.IsNullOrEmpty(term))
            return m;
        int p = 0;
        while ((p = expanded.IndexOf(term, p, StringComparison.Ordinal)) >= 0)
        {
            for (int j = p; j < p + term.Length && j < m.Length; j++)
                m[j] = true;
            p++;
        }
        return m;
    }

    /// <summary>
    /// Меню-бар в строке 0 (как menubar в MS Edit): меню слева, имя файла справа.
    /// Запоминает x-координаты меню для дропдауна.
    /// </summary>
    private void DrawMenuBar(int w)
    {
        List<TopMenu> menus = _menu?.Menus ?? BuildMenus();
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
                // Раскрытое меню — зелёная подсветка, как в MS Edit.
                _screen.Text(x, 0, cell, ConsoleColor.Black, ConsoleColor.Green);
            }
            else
            {
                _screen.Text(x, 0, " ", ConsoleColor.Gray, ConsoleColor.DarkBlue);
                WriteMenuCell(x + 1, menus[i].Label, menus[i].Hotkey);
                _screen.Text(x + cell.Length - 1, 0, " ", ConsoleColor.Gray, ConsoleColor.DarkBlue);
            }
            x += cell.Length;
        }
        string name = _buf.FilePath ?? "без имени";
        string dirty = _buf.IsModified ? "*" : string.Empty;
        string right = $" {name}{dirty} ";
        int rest = w - x;
        switch (rest, right.Length >= rest)
        {
            case (<= 0, _):
                break; // Места нет — имя файла не показываем.
            case (_, true):
                _screen.Text(x, 0, right[..rest], ConsoleColor.White, ConsoleColor.DarkBlue);
                break;
            default:
                _screen.Text(x, 0, new string(' ', rest - right.Length) + right, ConsoleColor.White, ConsoleColor.DarkBlue);
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
            _screen.Text(x, 0, label, ConsoleColor.Gray, ConsoleColor.DarkBlue);
            return;
        }
        _screen.Text(x, 0, label[..idx], ConsoleColor.Gray, ConsoleColor.DarkBlue);
        _screen.Text(x + idx, 0, label[idx..(idx + 1)], ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        _screen.Text(x + idx + 1, 0, label[(idx + 1)..], ConsoleColor.Gray, ConsoleColor.DarkBlue);
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

        ConsoleColor borderFg = ConsoleColor.Gray;
        ConsoleColor borderBg = ConsoleColor.Black;
        _screen.Text(x, y, "┌" + new string('─', boxW - 2) + "┐", borderFg, borderBg);
        for (int i = 0; i < rows; i++)
        {
            MenuItem it = m.Items[i];
            bool sel = i == _menu.SelectedIndex;
            // Выбранный пункт — зелёный, как в MS Edit.
            (ConsoleColor fg, ConsoleColor bg) = sel
                ? (ConsoleColor.Black, ConsoleColor.Green)
                : (ConsoleColor.Gray, ConsoleColor.Black);
            _screen.Text(x, y + 1 + i, "│", borderFg, borderBg);
            string right = " " + (it.Shortcut ?? it.Hotkey.ToString());
            int room = boxW - 2 - right.Length - 1;
            string label = " " + (it.Label.Length > room ? it.Label[..Math.Max(0, room)] : it.Label);
            _screen.Text(x + 1, y + 1 + i, label.PadRight(boxW - 2 - right.Length), fg, bg);
            _screen.Text(x + 1 + boxW - 2 - right.Length, y + 1 + i, right,
                sel ? fg : ConsoleColor.DarkGray, bg);
            _screen.Text(x + boxW - 1, y + 1 + i, "│", borderFg, borderBg);
        }
        _screen.Text(x, y + 1 + rows, "└" + new string('─', boxW - 2) + "┘", borderFg, borderBg);
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

        ConsoleColor bg = m.Danger ? ConsoleColor.DarkRed : ConsoleColor.DarkBlue;
        ConsoleColor fg = ConsoleColor.White;

        int btnWidth = m.Buttons.Sum(b => b.Label.Length + 4) + (m.Buttons.Count - 1) * 2;
        int content = m.Title.Length + 2;
        foreach (string line in m.Lines)
            content = Math.Max(content, line.Length);
        content = Math.Max(content, btnWidth);
        content = Math.Max(content, m.Hint.Length);
        int boxW = Math.Min(Math.Max(content + 6, 24), w);
        if (boxW < 12)
            return;
        int boxH = m.Lines.Count + 5; // верх, строки, кнопки, хинт, низ
        int x0 = Math.Max(0, (w - boxW) / 2);
        int y0 = Math.Max(0, (h - boxH) / 2);
        if (y0 + boxH > h)
            return;

        // Верх с заголовком: ┌─ Title ───┐
        string titleSeg = $" {m.Title} ";
        if (titleSeg.Length > boxW - 4)
            titleSeg = titleSeg[..Math.Max(0, boxW - 4)];
        _screen.Text(x0, y0, "┌─" + titleSeg + new string('─', Math.Max(0, boxW - 3 - titleSeg.Length)) + "┐", fg, bg);
        // Строки текста по центру.
        for (int i = 0; i < m.Lines.Count; i++)
            _screen.Text(x0, y0 + 1 + i, "│" + CenterPad(m.Lines[i], boxW - 2) + "│", fg, bg);
        // Кнопки по центру: [ Label ] с подсветкой выбранной.
        _screen.Text(x0, y0 + 1 + m.Lines.Count, "│" + new string(' ', boxW - 2) + "│", fg, bg);
        int used = 0;
        var cells = new List<string>();
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            string cell = $"[ {m.Buttons[i].Label} ]";
            cells.Add(cell);
            used += cell.Length + 2;
        }
        used -= 2;
        int padLeft = Math.Max(1, (boxW - 2 - used) / 2);
        var btnRow = new System.Text.StringBuilder();
        btnRow.Append('│');
        btnRow.Append(' ', padLeft);
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            if (i > 0)
                btnRow.Append("  ");
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
                _screen.Text(bx, btnY, cells[i], ConsoleColor.Black, ConsoleColor.Green);
            bx += cells[i].Length + 2;
        }
        // Хинт и низ.
        ConsoleColor hintFg = m.Danger ? ConsoleColor.Yellow : ConsoleColor.Gray;
        _screen.Text(x0, y0 + 3 + m.Lines.Count, "│" + CenterPad(m.Hint, boxW - 2) + "│", hintFg, bg);
        _screen.Text(x0, y0 + 4 + m.Lines.Count, "└" + new string('─', boxW - 2) + "┘", fg, bg);
    }

    private static string CenterPad(string s, int width)
    {
        if (s.Length >= width)
            return s[..Math.Max(0, width)];
        int left = (width - s.Length) / 2;
        return new string(' ', left) + s + new string(' ', width - s.Length - left);
    }

}
