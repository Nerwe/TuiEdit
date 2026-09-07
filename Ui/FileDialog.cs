namespace TuiEdit;

/// <summary>
/// Файловый менеджер модальным окном (как file-picker в MS Edit):
/// состояние — <see cref="FilePickerState"/>, исход — <see cref="Result"/>
/// (выбранный путь или null). Рамка, центрирование и цикл — из <see cref="Dialog"/>.
/// </summary>
internal sealed class FileDialog : Dialog
{
    private readonly FilePickerState _state;
    private readonly string _openTitle;
    private readonly string _saveTitle;
    private int _cursorX = -1;
    private int _cursorY = -1;

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
        return new DialogBox(Math.Max(0, (screenW - bw) / 2), Math.Max(0, (screenH - bh) / 2), bw, bh);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        FilePickerState p = _state;
        int x0 = box.X0, y0 = box.Y0, bw = box.W, bh = box.H;
        int inner = bw - 2;

        string dirLabel = p.CurrentDir == "" ? loc["picker.drives"] : p.CurrentDir;
        string dirRow = loc["picker.dir"] + MiddleTruncate(dirLabel, Math.Max(0, inner - loc["picker.dir"].Length));
        screen.Text(x0, y0 + 1, "│" + dirRow.PadRight(inner)[..inner] + "│", fg, bg);

        // Поле имени (хвост + курсор, как в промпте; выделение — инверсией).
        string nameTag = loc["picker.name"];
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
            screen.Set(x0 + 1 + i, y0 + 2, ch, sel ? theme.SelFg : fg, sel ? theme.SelBg : bg);
        }
        screen.Text(x0, y0 + 2, "│", fg, bg);
        screen.Text(x0 + bw - 1, y0 + 2, "│", fg, bg);
        int ncx = x0 + 1 + nameTag.Length + p.NamePos - shift;
        _cursorX = ncx >= x0 + 1 && ncx < x0 + bw - 1 ? ncx : -1;
        _cursorY = y0 + 2;

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
                    ? (theme.DropSelFg, theme.DropSelBg)
                    : e.IsDir
                        ? (e.Name == ".." ? theme.PickerUpFg : theme.PickerDirFg, bg)
                        : (theme.PickerFileFg, bg);
                string label = e.DisplayName;
                if (label.Length > inner)
                    label = label[..Math.Max(0, inner)];
                screen.Text(x0, yy, "│", fg, bg);
                screen.Text(x0 + 1, yy, label.PadRight(inner)[..inner], efg, ebg);
                screen.Text(x0 + bw - 1, yy, "│", fg, bg);
            }
            else
            {
                string empty = p.Error == "BadPath" ? loc["picker.badpath"]
                    : p.Error ?? (p.Entries.Count == 0 ? loc["picker.empty"] : "");
                Rgb efg = p.Error is null ? theme.PickerEmptyFg : theme.PickerErrorFg;
                screen.Text(x0, yy, "│" + empty.PadRight(inner)[..inner] + "│", efg, bg);
            }
        }

        string hint = loc["picker.hint"];
        screen.Text(x0, y0 + bh - 2, "│" + CenterPad(hint, inner)[..inner] + "│", theme.PickerHintFg, bg);
    }

    public override (int x, int y)? Cursor => _cursorX >= 0 ? (_cursorX, _cursorY) : null;

    public override void Paste(string text) =>
        _state.InsertName(text.Replace("\r", "").Replace("\n", ""));

    public override void HandleKey(ConsoleKeyInfo key)
    {
        var k = key;
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
            // Ctrl в поле имени: по словам и удаление слов.
            switch (k.Key)
            {
                case ConsoleKey.LeftArrow: _state.MoveNameWord(-1, shift); break;
                case ConsoleKey.RightArrow: _state.MoveNameWord(1, shift); break;
                case ConsoleKey.Backspace: _state.DeleteNameWord(-1); break;
                case ConsoleKey.Delete: _state.DeleteNameWord(1); break;
            }
            return; // прочий Ctrl в менеджере не используется
        }
        if ((k.Modifiers & ConsoleModifiers.Control) != 0)
            return; // Ctrl+Alt (AltGr) в менеджере не используется
        FilePickerState p = _state;
        switch (k.Key)
        {
            case ConsoleKey.Escape: Closed = true; return;
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
