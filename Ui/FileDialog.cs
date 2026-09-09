namespace TuiEdit;

/// <summary>Файловый менеджер модальным окном: исход — <see cref="Result"/> (выбранный путь или null).</summary>
internal sealed class FileDialog : Dialog
{
    private readonly FilePickerState _state;
    private readonly string _openTitle;
    private readonly string _saveTitle;
    private int _cursorX = -1;
    private int _cursorY = -1;
    private string? _pendingDelete;
    private int _pendingCount;

    public FileDialog(FilePickerState state, string openTitle, string saveTitle)
    {
        _state = state;
        _openTitle = openTitle;
        _saveTitle = saveTitle;
    }

    /// <summary>Выбранный путь (null — отмена). Читать после закрытия.</summary>
    public string? Result { get; private set; }

    protected override string GetTitle(Loc loc) =>
        _state.Mode == PickerMode.Open ? _openTitle : _saveTitle;

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        int bw = Math.Min(Math.Max(screenW - 10, 30), screenW);
        int bh = Math.Min(Math.Max(screenH - 8, 14), screenH);
        return new DialogBox(Math.Max(0, (screenW - bw) / 2), TopY(screenH, bh), bw, bh);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        FilePickerState p = _state;
        int x0 = box.X0, y0 = box.Y0, bw = box.W, bh = box.H;
        int inner = bw - 2;

        string dirLabel = p.CurrentDir == "" ? loc["picker.drives"] : p.CurrentDir;
        string dirRow = loc["picker.dir"] + MiddleTruncate(dirLabel, Math.Max(0, inner - loc["picker.dir"].Length));
        screen.Text(x0, y0 + 1, "│" + Dialog.FitCell(dirRow, inner) + "│", fg, bg);

        string nameTag = loc["picker.name"];
        string full = nameTag + p.Name;
        int shift = Math.Max(0, full.Length - inner);
        p.GetNameSelection(out int selA, out int selB);
        for (int i = 0; i < inner; i++)
        {
            int fi = shift + i;
            char ch = fi < full.Length ? full[fi] : ' ';
            bool sel = false;
            if (fi >= nameTag.Length)
            {
                int ni = fi - nameTag.Length;
                sel = ni >= selA && ni < selB;
            }
            screen.Set(x0 + 1 + i, y0 + 2, ch, sel ? theme.SelFg : fg, sel ? theme.SelBg : bg);
        }
        screen.Text(x0, y0 + 2, "│", fg, bg);
        screen.Text(x0 + bw - 1, y0 + 2, "│", fg, bg);
        int ncx = x0 + 1 + nameTag.Length + p.NamePos - shift;
        _cursorX = ncx >= x0 + 1 && ncx < x0 + bw - 1 ? ncx : -1;
        _cursorY = y0 + 2;

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
                    ? (theme.DropSelFg, theme.DropSelBg)
                    : e.IsDir
                        ? (e.Name == ".." ? theme.PickerUpFg : theme.PickerDirFg, bg)
                        : (theme.PickerFileFg, bg);
                string label = e.DisplayName;
                string size = e.IsDir ? string.Empty : FormatSize(e.Size);
                int room = inner - (size.Length > 0 ? size.Length + 1 : 0);
                if (label.Length > room)
                    label = label[..Math.Max(0, room)];
                screen.Text(x0, yy, "│", fg, bg);
                screen.Text(x0 + 1, yy, Dialog.FitCell(label, room), efg, ebg);
                if (size.Length > 0)
                    screen.Text(x0 + 1 + room + 1, yy, size,
                        sel ? efg : theme.PickerHintFg, ebg);
                screen.Text(x0 + bw - 1, yy, "│", fg, bg);
            }
            else
            {
                string empty = p.Error == "BadPath" ? loc["picker.badpath"]
                    : p.Error ?? (p.Entries.Count == 0 ? loc["picker.empty"] : "");
                Rgb efg = p.Error is null ? theme.PickerEmptyFg : theme.PickerErrorFg;
                screen.Text(x0, yy, "│" + Dialog.FitCell(empty, inner) + "│", efg, bg);
            }
        }

        string hint;
        Rgb hintFg = theme.PickerHintFg;
        if (_pendingDelete is not null)
        {
            string count = _pendingCount > 1000 ? "1000+"
                : _pendingCount == 1 ? loc["modal.delete.one"]
                : $"{_pendingCount} {loc["modal.delete.many"]}";
            hint = loc.Format("modal.delete.confirm", _pendingDelete, count);
            hintFg = theme.PickerErrorFg;
        }
        else if (_state.NoticeKey is string nk)
        {
            hint = loc[nk];
            hintFg = theme.PickerErrorFg;
        }
        else
        {
            hint = loc["picker.hint"];
        }
        // Хинт по центру по ВИДИМОЙ длине (спаны `..` не считаем).
        int visLen = Math.Min(SpanWidth(hint), inner);
        int hx = x0 + 1 + Math.Max(0, (inner - visLen) / 2);
        screen.Text(x0, y0 + bh - 2, "│" + new string(' ', inner) + "│", hintFg, bg);
        WriteSpans(screen, hx, y0 + bh - 2, hint, hintFg, theme.AccentFg, bg, inner - (hx - x0 - 1));
    }

    public override (int x, int y)? Cursor => _cursorX >= 0 ? (_cursorX, _cursorY) : null;

    private static string FormatSize(long n) => n switch
    {
        < 0 => string.Empty,
        < 1024 => $"{n}",
        < 1024 * 1024 => $"{n / 1024}K",
        < 1024L * 1024 * 1024 => $"{n / (1024 * 1024)}M",
        _ => $"{n / (1024L * 1024 * 1024)}G",
    };

    public override void Paste(string text) =>
        _state.InsertName(text.Replace("\r", "").Replace("\n", ""));

    public override void HandleKey(ConsoleKeyInfo key)
    {
        var k = key;
        if (_pendingDelete is not null)
        {
            // Взведённое удаление: Y/Enter — удалить, всё остальное — отмена.
            if (k.Key == ConsoleKey.Y && (k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
                || k.Key == ConsoleKey.Enter)
            {
                string target = _pendingDelete;
                _pendingDelete = null;
                _state.DeletePath(target);
            }
            else
            {
                _pendingDelete = null;
            }
            return;
        }
        bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
        bool alt = (k.Modifiers & ConsoleModifiers.Alt) != 0;
        if (alt && (k.Modifiers & ConsoleModifiers.Control) == 0)
        {
            // Навигация по каталогам (Backspace текст не трогает).
            switch (k.Key)
            {
                case ConsoleKey.LeftArrow: _state.UpDir(); break;
                case ConsoleKey.RightArrow: _state.EnterDir(); break;
            }
            return;
        }
        if ((k.Modifiers & ConsoleModifiers.Control) != 0
            && (k.Modifiers & ConsoleModifiers.Alt) == 0)
        {
            switch (k.Key)
            {
                case ConsoleKey.LeftArrow: _state.MoveNameWord(-1, shift); break;
                case ConsoleKey.RightArrow: _state.MoveNameWord(1, shift); break;
                case ConsoleKey.Backspace: _state.DeleteNameWord(-1); break;
                case ConsoleKey.Delete: _state.DeleteNameWord(1); break;
                case ConsoleKey.H: _state.ShowHidden = !_state.ShowHidden; _state.Refresh(); break;
            }
            return;
        }
        if ((k.Modifiers & ConsoleModifiers.Control) != 0)
            return; // Ctrl+Alt (AltGr) в менеджере не используется
        FilePickerState p = _state;
        switch (k.Key)
        {
            case ConsoleKey.Escape: Closed = true; return;
            case ConsoleKey.F7:
                _state.NoticeKey = _state.MakeDir(_state.Name) switch
                {
                    "ok" => null,
                    "empty" => "picker.mkdir.empty",
                    "exists" => "picker.mkdir.exists",
                    _ => null, // error — текст уже в Error
                };
                return;
            case ConsoleKey.F8:
                string? target = _state.DeleteTarget();
                if (target is null)
                    return;
                _pendingDelete = target;
                _pendingCount = FilePickerState.CountItems(target);
                return;
            case ConsoleKey.UpArrow: p.MoveHighlight(-1); break;
            case ConsoleKey.DownArrow: p.MoveHighlight(1); break;
            case ConsoleKey.PageUp: p.MoveHighlight(-10); break;
            case ConsoleKey.PageDown: p.MoveHighlight(10); break;
            case ConsoleKey.Home:
                if (shift) p.HomeName(true);
                else p.GotoFirst();
                break;
            case ConsoleKey.End:
                if (shift) p.EndName(true);
                else p.GotoLast();
                break;
            case ConsoleKey.Enter:
                var (res, path) = p.Enter();
                if (res == PickerEnterResult.Accepted && path is not null)
                {
                    Result = path;
                    Closed = true;
                }
                break;
            case ConsoleKey.Backspace: p.Backspace(); break;
            case ConsoleKey.Delete: p.DeleteChar(); break;
            case ConsoleKey.LeftArrow: p.MoveNameCursor(-1, shift); break;
            case ConsoleKey.RightArrow: p.MoveNameCursor(1, shift); break;
            default:
                if (!char.IsControl(k.KeyChar))
                    p.InsertName(k.KeyChar.ToString());
                break;
        }
    }
}
