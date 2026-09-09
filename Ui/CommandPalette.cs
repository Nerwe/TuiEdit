namespace TuiEdit;

/// <summary>Строка палитры: настройка (со значением) или команда (с шорткатом).</summary>
internal abstract record PaletteEntry
{
    public abstract string Label(AppSettings settings, Loc loc);

    public abstract string Value(AppSettings settings, Loc loc);

    /// <summary>Текст для фильтра: подпись + значение/шорткат.</summary>
    public string MatchText(AppSettings settings, Loc loc) =>
        Label(settings, loc) + " " + Value(settings, loc);
}

/// <summary>Строка настройки (индекс — как в <see cref="SettingsModel"/>).</summary>
internal sealed record SettingEntry(int Row) : PaletteEntry
{
    public override string Label(AppSettings settings, Loc loc) => SettingsModel.Label(Row, loc);

    public override string Value(AppSettings settings, Loc loc) => SettingsModel.Value(Row, settings, loc);
}

/// <summary>Строка команды (подпись и шорткат уже локализованы вызывающим).</summary>
internal sealed record CommandEntry(EditorCommand Command, string LabelText, string? Shortcut) : PaletteEntry
{
    public override string Label(AppSettings settings, Loc loc) => LabelText;

    public override string Value(AppSettings settings, Loc loc) =>
        KeyMap.HintFor(Command) ?? Shortcut ?? string.Empty;
}

/// <summary>
/// Состояние палитры: фильтр + видимые записи + курсор. Чистое, без консоли.
/// </summary>
internal sealed class CommandPaletteState
{
    public string Filter { get; private set; } = string.Empty;

    /// <summary>Записи под фильтром (порядок — как в полном списке).</summary>
    public List<PaletteEntry> View { get; private set; } = [];

    /// <summary>Курсор (индекс в <see cref="View"/>).</summary>
    public int Selected { get; private set; }

    /// <summary>Начало видимого окна.</summary>
    public int Top { get; private set; }

    public void SetFilter(string filter)
    {
        Filter = filter;
    }

    /// <summary>
    /// Подменить видимый список: новый фильтр — курсор в начало,
    /// тот же (значения поменялись) — держим запись, если жива.
    /// </summary>
    public void ReplaceView(List<PaletteEntry> view, bool fresh)
    {
        PaletteEntry? prev = !fresh && Selected >= 0 && Selected < View.Count
            ? View[Selected]
            : null;
        View = view;
        if (prev is null)
        {
            Selected = 0;
            Top = 0;
            return;
        }
        int at = View.IndexOf(prev);
        Selected = at >= 0 ? at : 0;
        Top = 0;
    }

    /// <summary>Двинуть курсор на delta (окно дотягивается).</summary>
    public void Move(int delta, int maxList) => MoveTo(Selected + delta, maxList);

    public void MoveTo(int index, int maxList)
    {
        if (View.Count == 0)
        {
            Selected = 0;
            Top = 0;
            return;
        }
        Selected = Math.Clamp(index, 0, View.Count - 1);
        Top = Math.Clamp(Top, 0, Math.Max(0, View.Count - maxList));
        if (Selected < Top)
            Top = Selected;
        if (Selected >= Top + maxList)
            Top = Selected - maxList + 1;
    }
}

/// <summary>
/// Палитра команд: строка фильтра + единый список (меню, команды, настройки).
/// Настройка — шагнуть и остаться, команда — закрыть и выполнить.
/// </summary>
internal sealed class CommandPaletteDialog : Dialog
{
    private readonly CommandPaletteState _state = new();
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Action _onChanged;
    private readonly Action<EditorCommand> _onCommand;
    private Loc? _loc;
    private string _builtFilter = "\0"; // фильтр, под который собран View
    private int _cursorX = -1;
    private int _cursorY = -1;

    public CommandPaletteDialog(
        AppSettings settings, SettingsStore store, Action onChanged, Action<EditorCommand> onCommand)
    {
        _settings = settings;
        _store = store;
        _onChanged = onChanged;
        _onCommand = onCommand;
    }

    /// <summary>
    /// Полный список записей: меню (подписи и шорткаты уже локализованы),
    /// затем команды без меню, затем настройки.
    /// </summary>
    internal static List<PaletteEntry> AllEntries(AppSettings settings, Loc loc)
    {
        var all = new List<PaletteEntry>();
        foreach (TopMenu menu in TuiEditor.BuildMenus(loc))
        {
            foreach (MenuItem item in menu.Items)
            {
                if (item.IsSeparator)
                    continue;
                all.Add(new CommandEntry(item.Command, menu.Label + ": " + item.Label, item.Shortcut));
            }
        }
        all.Add(new CommandEntry(EditorCommand.ToggleSidebar, loc["palette.cmd.togglesidebar"], "Ctrl+B"));
        all.Add(new CommandEntry(EditorCommand.ToggleLineNumbers, loc["palette.cmd.togglelinenumbers"], "Alt+N"));
        all.Add(new CommandEntry(EditorCommand.ToggleWrap, loc["palette.cmd.togglewrap"], "Alt+Z"));
        all.Add(new CommandEntry(EditorCommand.ToggleWhitespace, loc["palette.cmd.togglewhitespace"], "Alt+."));
        all.Add(new CommandEntry(EditorCommand.ToggleFold, loc["palette.cmd.togglefold"], "Alt+-"));
        all.Add(new CommandEntry(EditorCommand.ToggleBookmark, loc["palette.cmd.togglebookmark"], "F2"));
        all.Add(new CommandEntry(EditorCommand.NextBookmark, loc["palette.cmd.nextbookmark"], "Shift+F2"));
        all.Add(new CommandEntry(EditorCommand.CompleteWord, loc["palette.cmd.completeword"], "Ctrl+Space"));
        all.Add(new CommandEntry(EditorCommand.DocStats, loc["palette.cmd.docstats"], "F4"));
        all.Add(new CommandEntry(EditorCommand.MoveLineUp, loc["palette.cmd.movelineup"], "Alt+↑"));
        all.Add(new CommandEntry(EditorCommand.MoveLineDown, loc["palette.cmd.movelinedown"], "Alt+↓"));
        all.Add(new CommandEntry(EditorCommand.FindNext, loc["palette.cmd.findnext"], "F3"));
        all.Add(new CommandEntry(EditorCommand.FindPrev, loc["palette.cmd.findprev"], "Shift+F3"));
        all.Add(new CommandEntry(EditorCommand.ListTabs, loc["palette.cmd.listtabs"], "Ctrl+P"));
        all.Add(new CommandEntry(EditorCommand.NextTab, loc["palette.cmd.nexttab"], "Ctrl+PgDn"));
        all.Add(new CommandEntry(EditorCommand.PrevTab, loc["palette.cmd.prevtab"], "Ctrl+PgUp"));
        all.Add(new CommandEntry(EditorCommand.SplitPane, loc["palette.cmd.splitpane"], "Alt+S"));
        all.Add(new CommandEntry(EditorCommand.NextPane, loc["palette.cmd.nextpane"], "F6"));
        all.Add(new CommandEntry(EditorCommand.PrevPane, loc["palette.cmd.prevpane"], "Shift+F6"));
        for (int i = 0; i < SettingsModel.Count; i++)
            all.Add(new SettingEntry(i));
        return all;
    }

    /// <summary>Чистый фильтр записей (подстрока по подписи и значению/шорткату).</summary>
    internal static List<PaletteEntry> ApplyFilter(
        IReadOnlyList<PaletteEntry> all, string filter, AppSettings settings, Loc loc)
    {
        var view = new List<PaletteEntry>();
        foreach (PaletteEntry e in all)
        {
            if (filter.Length == 0
                || e.MatchText(settings, loc).Contains(filter, StringComparison.OrdinalIgnoreCase))
                view.Add(e);
        }
        return view;
    }

    /// <summary>Пересобрать View под текущий фильтр (держит курсор при том же фильтре).</summary>
    private void Rebuild(Loc loc)
    {
        bool fresh = _builtFilter != _state.Filter;
        List<PaletteEntry> view = ApplyFilter(AllEntries(_settings, loc), _state.Filter, _settings, loc);
        _builtFilter = _state.Filter;
        _state.ReplaceView(view, fresh);
    }

    protected override string GetTitle(Loc loc) => loc["palette.title"];

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        if (screenW < 20 || screenH < 5)
            return null;
        _loc = loc;
        Rebuild(loc);
        int labelW = 0, valW = 0;
        foreach (PaletteEntry e in AllEntries(_settings, loc))
        {
            labelW = Math.Max(labelW, e.Label(_settings, loc).Length);
            valW = Math.Max(valW, e.Value(_settings, loc).Length);
        }
        string hint = loc["palette.hint"];
        int inner = Math.Max(1 + 2 + labelW + 2 + (valW + 4) + 1,
            Math.Max(SpanWidth(hint), GetTitle(loc).Length + 2));
        int boxW = Math.Min(Math.Max(inner + 2, 30), screenW);
        int maxList = MaxList(screenH);
        int list = Math.Clamp(_state.View.Count, 1, maxList);
        int boxH = list + 4; // рамка + фильтр + список + хинт
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = TopY(screenH, boxH);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        _loc = loc;
        Rebuild(loc);
        int maxList = MaxList(screen.Height);
        _state.MoveTo(_state.Selected, maxList);
        int x0 = box.X0, y0 = box.Y0, inner = box.W - 2;

        // Строка фильтра.
        string prompt = "❯ ";
        bool empty = _state.Filter.Length == 0;
        string shown = empty ? loc["palette.filter"] : _state.Filter;
        Rgb promptFg = empty ? theme.ModalHintFg : fg;
        string cell = (prompt + shown).PadRight(inner)[..Math.Max(0, inner)];
        screen.Text(x0, y0 + 1, "│", fg, bg);
        screen.Text(x0 + 1, y0 + 1, cell, promptFg, bg);
        screen.Text(x0 + box.W - 1, y0 + 1, "│", fg, bg);
        int ncx = x0 + 1 + prompt.Length + (empty ? 0 : _state.Filter.Length);
        _cursorX = ncx >= x0 + 1 && ncx < x0 + box.W - 1 ? ncx : -1;
        _cursorY = y0 + 1;

        // Видимые строки — теми же option-рядами, что настройки.
        var labels = new List<string>();
        var values = new List<string>();
        int end = Math.Min(_state.View.Count, _state.Top + maxList);
        for (int i = _state.Top; i < end; i++)
        {
            labels.Add(_state.View[i].Label(_settings, loc));
            values.Add(_state.View[i].Value(_settings, loc));
        }
        if (labels.Count == 0)
        {
            labels.Add(loc["palette.noresults"]);
            values.Add(string.Empty);
        }
        int selVis = _state.View.Count == 0 ? 0 : _state.Selected - _state.Top;
        var rowsBox = new DialogBox(x0, y0 + 1, box.W, box.H - 1);
        DrawOptionRows(screen, theme, rowsBox, [.. labels], [.. values], selVis);

        // Хинт снизу по центру.
        string hint = loc["palette.hint"];
        int visLen = Math.Min(SpanWidth(hint), inner);
        int hx = x0 + 1 + Math.Max(0, (inner - visLen) / 2);
        int hy = y0 + box.H - 2;
        screen.Text(x0, hy, "│" + new string(' ', inner) + "│", theme.ModalHintFg, bg);
        WriteSpans(screen, hx, hy, hint, theme.ModalHintFg, theme.AccentFg, bg, inner - (hx - x0 - 1));
    }

    public override (int x, int y)? Cursor => _cursorX >= 0 ? (_cursorX, _cursorY) : null;

    public override bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return false;
        DialogBox b = box.Value;
        if (x < b.X0 || x >= b.X0 + b.W || y < b.Y0 || y >= b.Y0 + b.H)
            return false;
        int maxList = MaxList(screenH);
        int row = y - (b.Y0 + 2); // заголовок + фильтр
        int end = Math.Min(_state.View.Count, _state.Top + maxList);
        if (row < 0 || _state.Top + row >= end)
            return true; // фильтр/хинт/пусто — глушим
        _state.MoveTo(_state.Top + row, maxList);
        Activate();
        return true;
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        var k = key;
        if ((k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return;
        switch (k.Key)
        {
            case ConsoleKey.Escape:
                if (_state.Filter.Length > 0)
                    _state.SetFilter(string.Empty); // сначала чистим фильтр
                else
                    Closed = true;
                return;
            case ConsoleKey.Enter:
                Activate();
                return;
            case ConsoleKey.UpArrow: _state.Move(-1, MaxListFallback()); break;
            case ConsoleKey.DownArrow: _state.Move(1, MaxListFallback()); break;
            case ConsoleKey.Home: _state.MoveTo(0, MaxListFallback()); break;
            case ConsoleKey.End: _state.MoveTo(int.MaxValue, MaxListFallback()); break;
            case ConsoleKey.PageUp: _state.Move(-5, MaxListFallback()); break;
            case ConsoleKey.PageDown: _state.Move(5, MaxListFallback()); break;
            case ConsoleKey.Backspace:
                if (_state.Filter.Length > 0)
                    _state.SetFilter(_state.Filter[..^1]);
                return;
            default:
                if (!char.IsControl(k.KeyChar))
                {
                    string next = _state.Filter + k.KeyChar;
                    if (next.Length <= 64)
                        _state.SetFilter(next);
                }
                return;
        }
    }

    public override void Paste(string text)
    {
        string next = _state.Filter + text.Replace("\r", "").Replace("\n", "");
        _state.SetFilter(next.Length <= 64 ? next : next[..64]);
    }

    private static int MaxList(int screenH) =>
        Math.Max(3, Math.Min(60, screenH - 10));

    /// <summary>Окно списка неизвестно без размеров экрана — оценка для клавиш (Draw доклампит).</summary>
    private static int MaxListFallback() => 60;

    /// <summary>Настройка — шагнуть и остаться, команда — закрыть и отдать редактору.</summary>
    private void Activate()
    {
        if (_state.View.Count == 0)
            return;
        _state.MoveTo(_state.Selected, MaxListFallback());
        PaletteEntry e = _state.View[_state.Selected];
        if (e is SettingEntry s)
        {
            SettingsModel.Cycle(_settings, s.Row, 1);
            _store.Save(_settings);
            _onChanged();
            if (_loc is not null)
                Rebuild(_loc); // значения поменялись — курсор держим по записи
            return;
        }
        if (e is CommandEntry c)
        {
            Closed = true;
            _onCommand(c.Command);
        }
    }
}
