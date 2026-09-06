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

    private const int TabSize = 4;

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
            Render(); // первичная отрисовка
            while (!_quitRequested)
            {
                Render();
                ConsoleKeyInfo k;
                try
                {
                    k = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    // Ввод перенаправлен — работать не можем (см. docs System.Console).
                    return;
                }
                HandleKey(k);
            }
        }
        finally
        {
            Console.ResetColor();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }

    // ---------- Ввод ----------

    private void HandleKey(ConsoleKeyInfo k)
    {
        // Маршрутизация клавиш — через чистый KeyMap (см. KeyMap.cs),
        // чтобы маппинг можно было проверять unit-тестами без консоли.
        switch (KeyMap.Map(k))
        {
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
            case EditorCommand.Find: Find(); return;
            case EditorCommand.FindNext: FindNext(); return;
            case EditorCommand.GoToLine: GoToLine(); return;
            case EditorCommand.CutLine: CutLine(); return;
            case EditorCommand.CopyLine: CopyLine(); return;
            case EditorCommand.Paste: Paste(); return;
            case EditorCommand.SaveAs: SaveAs(); return;
            case EditorCommand.Undo: _buf.Undo(); SetMessage("Отмена"); return;
            case EditorCommand.Redo: _buf.Redo(); SetMessage("Возврат"); return;
            case EditorCommand.InsertEnter:
                (_row, _col) = _buf.SplitLine(_row, _col);
                _desiredCol = _col;
                return;
            case EditorCommand.InsertBackspace:
                (_row, _col) = _buf.Backspace(_row, _col);
                _desiredCol = _col;
                return;
            case EditorCommand.InsertDelete:
                (_row, _col) = _buf.Delete(_row, _col);
                _desiredCol = _col;
                return;
            case EditorCommand.InsertTab:
                _buf.InsertString(_row, _col, new string(' ', TabSize));
                _col += TabSize; _desiredCol = _col;
                return;
            case EditorCommand.InsertChar:
                _buf.InsertChar(_row, _col, k.KeyChar);
                _col++;
                _desiredCol = _col;
                return;
            case EditorCommand.None:
            default: return; // Esc вне диалога, Alt, неизвестные Ctrl-комбинации — ничего
        }
    }

    // ---------- Движения ----------

    private string CurLine => _buf.GetLine(_row);

    private void ClampCursor()
    {
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Clamp(_col, 0, _buf.GetLine(_row).Length);
    }

    private void MoveLeft()
    {
        ClampCursor();
        if (_col > 0) _col--;
        else if (_row > 0) { _row--; _col = _buf.GetLine(_row).Length; }
        _desiredCol = _col;
    }

    private void MoveRight()
    {
        ClampCursor();
        if (_col < CurLine.Length) _col++;
        else if (_row < _buf.Count - 1) { _row++; _col = 0; }
        _desiredCol = _col;
    }

    private void MoveUp()
    {
        if (_row > 0) { _row--; _col = Math.Min(_desiredCol, _buf.GetLine(_row).Length); }
    }

    private void MoveDown()
    {
        if (_row < _buf.Count - 1) { _row++; _col = Math.Min(_desiredCol, _buf.GetLine(_row).Length); }
    }

    private void GoHome() { ClampCursor(); _col = 0; _desiredCol = 0; }
    private void GoEnd() { ClampCursor(); _col = CurLine.Length; _desiredCol = _col; }
    private void GoDocStart() { _row = 0; _col = 0; _desiredCol = 0; }
    private void GoDocEnd() { _row = _buf.Count - 1; _col = _buf.GetLine(_row).Length; _desiredCol = _col; }

    private void MovePage(int dir)
    {
        int h = TextHeight();
        _row = Math.Clamp(_row + dir * Math.Max(1, h - 1), 0, _buf.Count - 1);
        _col = Math.Min(_desiredCol, _buf.GetLine(_row).Length);
    }

    private void MoveWordLeft()
    {
        ClampCursor();
        if (_col == 0 && _row > 0) { _row--; _col = _buf.GetLine(_row).Length; }
        string line = CurLine;
        int i = _col;
        while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
        while (i > 0 && TextBuffer.IsWordChar(line[i - 1])) i--;
        _col = i; _desiredCol = _col;
    }

    private void MoveWordRight()
    {
        ClampCursor();
        string line = CurLine;
        int i = _col;
        while (i < line.Length && TextBuffer.IsWordChar(line[i])) i++;
        while (i < line.Length && !TextBuffer.IsWordChar(line[i]) && !char.IsWhiteSpace(line[i])) i++;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        if (i >= line.Length && _row < _buf.Count - 1) { _row++; _col = 0; }
        else _col = i;
        _desiredCol = _col;
    }

    private void DeleteWordBefore()
    {
        ClampCursor();
        if (_col == 0 && _row == 0) return;
        int origRow = _row, origCol = _col;
        MoveWordLeft();
        // Удаляем диапазон [(_row,_col), (origRow,origCol)] — пока только внутри одной строки.
        if (_row == origRow)
        {
            int target = _col;
            int n = origCol - target;
            _row = origRow;
            _col = origCol;
            for (int i = 0; i < n; i++)
                (_row, _col) = _buf.Backspace(_row, _col);
        }
        else
        {
            // Переход через строку — обычная склейка.
            (_row, _col) = _buf.Backspace(origRow, origCol);
        }
        _desiredCol = _col;
    }

    private void DeleteWordAfter()
    {
        ClampCursor();
        string line = CurLine;
        int i = _col;
        if (i >= line.Length)
        {
            _buf.Delete(_row, _col);
            return;
        }
        int start = i;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        if (i < line.Length && TextBuffer.IsWordChar(line[i]))
            while (i < line.Length && TextBuffer.IsWordChar(line[i])) i++;
        else if (i < line.Length)
            while (i < line.Length && !TextBuffer.IsWordChar(line[i]) && !char.IsWhiteSpace(line[i])) i++;
        for (int n = 0; n < i - start; n++)
            (_row, _col) = _buf.Delete(_row, _col);
        _desiredCol = _col;
    }

    // ---------- Команды ----------

    private void Save()
    {
        if (_buf.FilePath is null) { SaveAs(); return; }
        try
        {
            _buf.Save();
            SetMessage($"Сохранено: {_buf.FilePath}");
        }
        catch (Exception ex) { SetMessage($"Ошибка сохранения: {ex.Message}"); }
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
        if (_buf.IsModified)
        {
            // Подтверждение одной клавишей: Enter — выйти, Esc — остаться.
            if (!Confirm("Выйти без сохранения?")) { SetMessage("Выход отменён"); return; }
        }
        _quitRequested = true;
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
            _desiredCol = _col;
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
            _desiredCol = _col;
        }
        else SetMessage("Не число");
    }

    private void CutLine()
    {
        ClampCursor();
        _clipboard.Clear();
        _clipboard.Add(_buf.CutLine(_row));
        _row = Math.Clamp(_row, 0, _buf.Count - 1);
        _col = Math.Min(_col, _buf.GetLine(_row).Length);
        _desiredCol = _col;
        SetMessage("Строка вырезана (^U — вставить)");
    }

    private void CopyLine()
    {
        ClampCursor();
        _clipboard.Clear();
        _clipboard.Add(_buf.GetLine(_row));
        SetMessage("Строка скопирована");
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) { SetMessage("Буфер пуст"); return; }
        ClampCursor();
        _buf.PasteLines(_row, _col, _clipboard);
        if (_clipboard.Count == 1) _col += _clipboard[0].Length;
        else { _row += _clipboard.Count - 1; _col = _clipboard[^1].Length; }
        _desiredCol = _col;
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
            var k = Console.ReadKey(intercept: true);
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
        int w, h;
        try { w = Console.WindowWidth; h = Console.WindowHeight; }
        catch { return; }
        if (w < 10 || h < 4) return;
        int row = h - 2;
        Console.SetCursorPosition(0, row);
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        string text = title + input;
        if (text.Length > w) text = text[^w..];
        Console.Write(text.PadRight(w));
        Console.ResetColor();
        int cursorX = Math.Min(w - 1, title.Length + pos - Math.Max(0, (title.Length + input.Length) - w));
        try
        {
            Console.SetCursorPosition(Math.Max(0, cursorX), row);
            Console.CursorVisible = true;
        }
        catch { }
    }

    /// <summary>
    /// Одноклавишное подтверждение: Enter — да, Esc — нет.
    /// Буквы Y/Д/N/Н тоже работают сразу, без дополнительного Enter.
    /// </summary>
    private bool Confirm(string title)
    {
        while (true)
        {
            DrawConfirm(title);
            var k = Console.ReadKey(intercept: true);
            if (k.Key is ConsoleKey.Enter) return true;
            if (k.Key is ConsoleKey.Escape) return false;
            if (k.KeyChar is 'y' or 'Y' or 'д' or 'Д') return true;
            if (k.KeyChar is 'n' or 'N' or 'н' or 'Н') return false;
            // Остальные клавиши игнорируем и ждём Enter/Esc.
        }
    }

    private void DrawConfirm(string title)
    {
        int w, h;
        try { w = Console.WindowWidth; h = Console.WindowHeight; }
        catch { return; }
        if (w < 10 || h < 4) return;
        int row = h - 2;
        Console.SetCursorPosition(0, row);
        Console.BackgroundColor = ConsoleColor.DarkRed;
        Console.ForegroundColor = ConsoleColor.White;
        string text = $"{title}  [Enter — да / Esc — нет]";
        if (text.Length > w) text = text[..w];
        Console.Write(text.PadRight(w));
        Console.ResetColor();
        try
        {
            Console.SetCursorPosition(Math.Min(w - 1, text.Length), row);
            Console.CursorVisible = true;
        }
        catch { }
    }

    // ---------- Отрисовка ----------

    private int TextHeight()
    {
        int h;
        try { h = Console.WindowHeight; } catch { return 10; }
        return Math.Max(1, h - 3);
    }

    private void EnsureVisible(int textHeight, int contentWidth)
    {
        ClampCursor();
        if (_row < _top) _top = _row;
        if (_row >= _top + textHeight) _top = _row - textHeight + 1;
        if (_col < _left) _left = _col;
        if (_col >= _left + contentWidth) _left = _col - contentWidth + 1;
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
        if (w < 20 || h < 6)
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

        int textHeight = h - 3;
        int numWidth = Math.Max(4, _buf.Count.ToString().Length);
        int gutterWidth = numWidth + 3; // "1234 │ "
        int contentWidth = Math.Max(1, w - gutterWidth);

        EnsureVisible(textHeight, contentWidth);
        Console.CursorVisible = false;

        // Верхняя панель
        Console.SetCursorPosition(0, 0);
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        string name = _buf.FilePath ?? "без имени";
        string dirty = _buf.IsModified ? " *" : string.Empty;
        string title = $" TuiEdit  —  {name}{dirty}";
        string right = $" {(_row + 1)}/{_buf.Count} ";
        string titleLine = title.Length + right.Length > w
            ? title[..Math.Max(0, w - right.Length)] + right
            : title.PadRight(w - right.Length) + right;
        Console.Write(titleLine[..Math.Min(w, titleLine.Length)].PadRight(w));

        // Текст
        for (int i = 0; i < textHeight; i++)
        {
            int fileLine = _top + i;
            Console.SetCursorPosition(0, 1 + i);
            Console.BackgroundColor = ConsoleColor.Black;
            if (fileLine >= _buf.Count)
            {
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write(("~".PadRight(w))[..w]);
                continue;
            }
            // Номер строки
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{(fileLine + 1).ToString().PadLeft(numWidth)} │ ");

            string line = _buf.GetLine(fileLine);
            string visible = _left < line.Length
                ? line.Substring(_left, Math.Min(contentWidth, line.Length - _left))
                : string.Empty;

            bool isCur = fileLine == _row;
            string searched = _lastSearch;
            if (isCur)
            {
                // Подсветка текущей строки — тёмный фон.
                Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Gray;
            }

            // Простая подсветка найденного фрагмента на видимых строках.
            if (!string.IsNullOrEmpty(searched) && visible.Contains(searched, StringComparison.Ordinal))
            {
                int idx = 0;
                int rest = contentWidth;
                string v = visible;
                // Печатаем по кускам, подсвечивая совпадения жёлтым.
                while (v.Length > 0 && rest > 0)
                {
                    int p = v.IndexOf(searched, StringComparison.Ordinal);
                    string chunk = p < 0 ? v : v[..p];
                    string toWrite = chunk.Length > rest ? chunk[..rest] : chunk;
                    Console.Write(toWrite);
                    rest -= toWrite.Length;
                    if (rest <= 0) break;
                    if (p < 0) break;
                    string hl = searched.Length > rest ? searched[..rest] : searched;
                    var bg = Console.BackgroundColor;
                    Console.BackgroundColor = ConsoleColor.DarkYellow;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write(hl);
                    Console.BackgroundColor = bg;
                    Console.ForegroundColor = isCur ? ConsoleColor.White : ConsoleColor.Gray;
                    rest -= hl.Length;
                    v = v[(p + searched.Length)..];
                    idx++;
                    if (idx > 50) { break; } // защита от патологий
                }
                int pad = rest;
                if (pad > 0) Console.Write(new string(' ', pad));
            }
            else
            {
                Console.Write(visible.PadRight(contentWidth)[..contentWidth]);
            }
        }

        // Строка статуса/сообщений
        Console.SetCursorPosition(0, h - 2);
        Console.BackgroundColor = ConsoleColor.Gray;
        Console.ForegroundColor = ConsoleColor.Black;
        string msg = CurrentMessage;
        string pos = $"Стр {_row + 1}/{_buf.Count} Стлб {_col + 1}";
        string status = string.IsNullOrEmpty(msg) ? $" {pos}  |  F2-сохранить  F3-далее  ^Q-выход" : $" {msg}";
        if (status.Length > w) status = status[..w];
        Console.Write(status.PadRight(w));

        // Панель подсказок
        Console.SetCursorPosition(0, h - 1);
        Console.BackgroundColor = ConsoleColor.Black;
        DrawHelp(w);

        // Аппаратный курсор
        int cx = gutterWidth + (_col - _left);
        int cy = 1 + (_row - _top);
        if (cy >= 1 && cy < 1 + textHeight && cx >= gutterWidth && cx < w)
        {
            try
            {
                Console.SetCursorPosition(cx, cy);
                Console.CursorVisible = true;
            }
            catch { }
        }
    }

    private static void DrawHelp(int w)
    {
        (string key, string desc)[] items =
        [
            ("F2", "Сохранить"),
            ("^O", "Как"),
            ("^F", "Найти"),
            ("^G", "Строка"),
            ("^K", "Вырез"),
            ("^U", "Встав"),
            ("^Z", "Отмена"),
            ("^Q", "Выход"),
        ];
        int x = 0;
        foreach (var (key, desc) in items)
        {
            string seg = $" {key} {desc} ";
            if (x + seg.Length > w) break;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($" {key}");
            x += key.Length + 1;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            string d = $" {desc} ";
            if (x + d.Length > w) break;
            Console.Write(d);
            x += d.Length;
        }
        int rest = w - x;
        if (rest > 0) Console.Write(new string(' ', rest));
        Console.ResetColor();
    }
}
