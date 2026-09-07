// TUI-движок на чистом System.Console.
// Используемые API из Microsoft Learn (System.Console):
// ReadKey(true), SetCursorPosition, WindowWidth/WindowHeight, CursorVisible,
// ForegroundColor/BackgroundColor, TreatControlCAsInput, OutputEncoding, Clear, ResetColor.

using System.Text.RegularExpressions;

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
    private Dialog? _dialog;    // null — диалогового окна нет (модалка/менеджер/настройки)
    private PendingOp _pending = PendingOp.None;
    private string _pendingPath = string.Empty;
    private string _overwritePath = string.Empty; // путь из модалки перезаписи
    private readonly List<string> _recentPaths = new(); // пути из модалки недавних
    private readonly DraftStore _drafts = new(DraftStore.DefaultDir());
    private DateTime _lastDraftAt = DateTime.MinValue;
    private readonly List<(string key, DocDraft draft)> _restoreDrafts = new();
    private readonly List<int> _menuX = new(); // x-координаты меню в баре (из рендера)
    private readonly Screen _screen = new(); // кадр + diff-вывод (без мигания)
    private readonly InputReader _input = new();
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private Loc _loc;
    private Theme _theme;
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
        Terminal.TryEnableRawInput(); // Ctrl+S мимо XOFF-паузы, как в MS Edit

        try
        {
            // Bracketed paste (как в MS Edit: "\x1b[?2004h"): вставка из обмена
            // приходит одним событием и кладётся полным текстом, а не посимвольно.
            try { Console.Write("\x1b[?2004h"); } catch (IOException) { }
            Render(); // первичная отрисовка
            MaybeRestore(); // черновики после краша (если есть)
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
                AutoDraft(); // черновик грязного буфера, не чаще раза в 30 с
            }
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

    /// <summary>Пора ли писать черновик (чистая функция для тестов).</summary>
    internal static bool DraftDue(DateTime last, DateTime now) =>
        (now - last).TotalSeconds >= 30;

    /// <summary>Черновик грязного буфера (тихо, с троттлингом).</summary>
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

    /// <summary>Предложить восстановление черновиков на старте (буфер всегда чист).</summary>
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

    /// <summary>Применить черновик из модалки восстановления (индекс кнопки).</summary>
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
            case EditorCommand.OpenRecent: DoRecent(); return;
            case EditorCommand.About: _dialog = new ModalDialog(ModalState.About(_loc, AppVersion), ApplyModalOutcome); return;
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

    /// <summary>Отметить файл недавним и сохранить настройки.</summary>
    private void TouchRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _settings.TouchRecent(path);
        _store.Save(_settings);
    }

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
                    _buf.Save(backup: _settings.BackupOnSave);
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
            _buf.Save(path, _settings.BackupOnSave);
            TouchRecent(_buf.FilePath);
            _drafts.Delete(_buf.FilePath);
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
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc), ApplyModalOutcome);
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
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc), ApplyModalOutcome);
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
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc), ApplyModalOutcome);
    }

    /// <summary>Недавние файлы модалкой (пустой список — сообщение).</summary>
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

    /// <summary>Выбор из модалки недавних (индекс кнопки).</summary>
    private void OpenRecentPick(int button)
    {
        if (_recentPaths.Count == 0)
            return;
        string path = _recentPaths[Math.Clamp(button, 0, _recentPaths.Count - 1)];
        _recentPaths.Clear();
        if (!_buf.IsModified)
        {
            LoadFile(path);
            return;
        }
        _pending = PendingOp.Open;
        _pendingPath = path;
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc), ApplyModalOutcome);
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
            TouchRecent(path);
            SetMessage(existed ? _loc.Format("msg.opened", path) : _loc.Format("msg.newfile", path));
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.open"], DisplayError(ex)), ApplyModalOutcome);
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
        // Всё остальное (Ctrl-шорткаты, F3...) — глобально: закрыть меню и выполнить.
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
            new(loc["menu.recent"], 'R', null, EditorCommand.OpenRecent),
            new(loc["menu.save"], 'S', "^S", EditorCommand.Save),
            new(loc["menu.saveas"], 'A', "Ctrl+Shift+S", EditorCommand.SaveAs),
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

    /// <summary>Исход закрытой модалки: кнопки и отмена (pending-действия живут здесь).</summary>
    private void ApplyModalOutcome(ModalState m, ModalKeyOutcome o)
    {
        if (o.Cancelled)
        {
            // Esc: всё отменяется (включая ожидание перезаписи и список недавних).
            _overwritePath = string.Empty;
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
                        SaveFlowForPending(); // Сохранить
                        return;
                    case UnsavedAction.Discard:
                        _drafts.Delete(_buf.FilePath); // черновик больше не нужен
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
                    _buf.Save(_overwritePath, _settings.BackupOnSave); // Да — перезаписать
                    TouchRecent(_buf.FilePath);
                    _drafts.Delete(_buf.FilePath);
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex)
                {
                    // Не сохранилось — отменяем всё, чтобы не потерять данные выходом.
                    _overwritePath = string.Empty;
                    _pending = PendingOp.None;
                    _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.save"], DisplayError(ex)), ApplyModalOutcome);
                    return;
                }
                _overwritePath = string.Empty;
                ApplyPending();
                return;
            case (ModalKind.Recent, var b):
                OpenRecentPick(b);
                return;
            case (ModalKind.Restore, var b):
                ApplyRestore(b);
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
                        _dialog = new ModalDialog(ModalState.Overwrite(_loc, Path.GetFileName(path)), ApplyModalOutcome);
                        return;
                    }
                    _buf.Save(path, _settings.BackupOnSave);
                    break;
                default:
                    _buf.Save(backup: _settings.BackupOnSave);
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
    /// Общий драйвер диалоговых окон: ставит диалог, качает Render/Read,
    /// пока не закроется. Заменяет дублировавшиеся циклы менеджера и настроек.
    /// </summary>
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
                    ev = _input.Read();
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

    /// <summary>
    /// Файловый менеджер модальным окном (свой цикл ввода, как Prompt).
    /// В Save-режиме перезапись подтверждается внутри (красный бокс).
    /// </summary>
    /// <returns>Выбранный путь или null (Esc).</returns>
    private string? RunPicker(PickerMode mode, string startDir, string initialName)
    {
        var dlg = new FileDialog(new FilePickerState(mode, startDir, initialName),
            _loc["picker.open.title"], _loc["picker.save.title"]);
        RunDialog(dlg);
        return dlg.Result;
    }

    /// <summary>Путь для сохранения через менеджер (перезапись подтверждает модалка).</summary>
    private string? PickSavePath(string initialName) =>
        RunPicker(PickerMode.Save, StartDir(), initialName);

    private void Find()
    {
        string? term = Prompt(_loc["prompt.find"], _lastSearch);
        if (term is null) { SetMessage(_loc["msg.search.cancelled"]); return; }
        if (term.Length == 0) { SetMessage(_loc["msg.search.empty"]); return; }
        if (BadPattern(term)) return;
        _lastSearch = term;
        FindNext();
    }

    /// <summary>Плохой regex-шаблон? Показывает сообщение и возвращает true.</summary>
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

    /// <summary>Прыжок к следующему/предыдущему вхождению со счётчиком «k/N».</summary>
    private void JumpSearch(bool wrap, bool backward)
    {
        if (string.IsNullOrEmpty(_lastSearch)) { Find(); return; }
        bool mc = _settings.SearchMatchCase, ww = _settings.SearchWholeWord, rx = _settings.SearchUseRegex;
        var hit = backward
            ? _buf.FindPrev(_lastSearch, _row, _col, mc, ww, wrap, rx)
            : _buf.FindNext(_lastSearch, _row, _col + 1, mc, ww, wrap, rx);
        if (hit is null) { SetMessage(_loc.Format("msg.search.miss", _lastSearch)); return; }
        (_row, _col) = (hit.Value.row, hit.Value.col);
        _sel.Clear(); // прыжок снимает выделение
        TrackCol();
        int total = _buf.CountMatches(_lastSearch, mc, ww, rx);
        int idx = _buf.MatchOrdinal(_lastSearch, _row, _col, mc, ww, rx);
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
        if (BadPattern(term)) return;
        _lastSearch = term;
        _lastReplace = rep;
        int n = _buf.ReplaceAll(term, rep, 0, 0,
            _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
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

        // Поверх текста: раскрытое меню и активное диалоговое окно.
        DrawDropdown(w, h);
        _dialog?.Draw(_screen, _theme, _loc);

        // Один diff-вывод за кадр — без мигания.
        _screen.Flush();

        // Аппаратный курсор ставим один раз за кадр:
        // диалог с курсором (поле имени менеджера) — туда, иначе текст
        // (прячем под меню, диалогом и справкой).
        bool uiOpen = _menu is not null || _dialog is not null || _helpOpen;
        string curLine = _buf.GetLine(_row);
        int vcolCur = TabStops.VisualWidth(curLine, _col);
        int curBase = wrap
            ? WordWrap.SegmentStarts(curLine, contentWidth)[CursorSeg(curLine, _col, contentWidth)]
            : _left;
        int cx = gutterWidth + (vcolCur - curBase);
        int cy = 1 + CursorVisualRow(_buf.Lines, _top, _topSeg, _row, _col, contentWidth, wrap, textHeight);
        (int x, int y)? dlgCursor = _dialog?.Cursor;
        bool pickerCursor = dlgCursor is not null;
        bool placed = pickerCursor
            || (!uiOpen && cy >= 1 && cy < 1 + textHeight && cx >= gutterWidth && cx < w);
        try
        {
            if (placed)
                Console.SetCursorPosition(pickerCursor ? dlgCursor!.Value.x : cx, pickerCursor ? dlgCursor!.Value.y : cy);
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
                _lastSearch, _settings.SearchMatchCase, _settings.SearchWholeWord, _settings.SearchUseRegex);
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
    private static bool[] FindMatches(string expanded, string term, bool matchCase, bool wholeWord, bool useRegex)
    {
        var m = new bool[expanded.Length];
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
                    int len = Math.Max(1, mt.Length); // нулевое — один символ
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

    /// <summary>Диалог настроек общим драйвером (рисуется внутри Render).</summary>
    private void RunSettings()
    {
        RunDialog(new SettingsDialog(_settings, _store, ApplySettings));
    }

    private string OnOff(bool v) => v ? _loc["settings.on"] : _loc["settings.off"];

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
}
