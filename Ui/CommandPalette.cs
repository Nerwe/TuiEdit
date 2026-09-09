namespace TuiEdit;

/// <summary>
/// Состояние палитры: фильтр + видимые строки + курсор. Чистое, без консоли.
/// </summary>
internal sealed class CommandPaletteState
{
    public string Filter { get; private set; } = string.Empty;

    /// <summary>Индексы строк настроек под фильтром (порядок — как в диалоге).</summary>
    public List<int> View { get; private set; } = AllRows();

    /// <summary>Курсор (индекс в <see cref="View"/>).</summary>
    public int Selected { get; private set; }

    /// <summary>Начало видимого окна.</summary>
    public int Top { get; private set; }

    private string _builtFor = "\0"; // фильтр, под который собран View

    private static List<int> AllRows()
    {
        var rows = new List<int>(SettingsModel.Count);
        for (int i = 0; i < SettingsModel.Count; i++)
            rows.Add(i);
        return rows;
    }

    /// <summary>
    /// Пересчитать видимые строки (подстрока по подписи и значению).
    /// Новый фильтр — курсор в начало; тот же — держим строку, если жива.
    /// </summary>
    public void Refilter(AppSettings settings, Loc loc)
    {
        int? prevRow = View.Count > 0 && Selected >= 0 && Selected < View.Count
            ? View[Selected]
            : null;
        bool fresh = _builtFor != Filter;
        View = [];
        for (int i = 0; i < SettingsModel.Count; i++)
        {
            if (Filter.Length == 0
                || SettingsModel.Label(i, loc).Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || SettingsModel.Value(i, settings, loc).Contains(Filter, StringComparison.OrdinalIgnoreCase))
                View.Add(i);
        }
        _builtFor = Filter;
        if (fresh || prevRow is null)
        {
            Selected = 0;
            Top = 0;
            return;
        }
        int at = View.IndexOf(prevRow.Value);
        Selected = at >= 0 ? at : 0;
        Top = 0;
    }

    public void SetFilter(string filter)
    {
        Filter = filter;
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
/// Палитра команд (пока — поиск по настройкам): строка фильтра + список,
/// Enter — следующее значение (как стрелка вправо в диалоге настроек).
/// </summary>
internal sealed class CommandPaletteDialog : Dialog
{
    private readonly CommandPaletteState _state = new();
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Action _onChanged;
    private Loc? _loc;
    private int _cursorX = -1;
    private int _cursorY = -1;

    public CommandPaletteDialog(AppSettings settings, SettingsStore store, Action onChanged)
    {
        _settings = settings;
        _store = store;
        _onChanged = onChanged;
    }

    protected override string GetTitle(Loc loc) => loc["palette.title"];

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        if (screenW < 20 || screenH < 5)
            return null;
        _loc = loc;
        _state.Refilter(_settings, loc);
        int labelW = 0, valW = 0;
        for (int i = 0; i < SettingsModel.Count; i++)
        {
            labelW = Math.Max(labelW, SettingsModel.Label(i, loc).Length);
            valW = Math.Max(valW, SettingsModel.Value(i, _settings, loc).Length);
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
        _state.Refilter(_settings, loc);
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
        int ncx = x0 + 1 + prompt.Length + _state.Filter.Length;
        if (empty)
            ncx = x0 + 1 + prompt.Length;
        _cursorX = ncx >= x0 + 1 && ncx < x0 + box.W - 1 ? ncx : -1;
        _cursorY = y0 + 1;

        // Видимые строки — теми же option-рядами, что настройки.
        var labels = new List<string>();
        var values = new List<string>();
        int end = Math.Min(_state.View.Count, _state.Top + maxList);
        for (int i = _state.Top; i < end; i++)
        {
            labels.Add(SettingsModel.Label(_state.View[i], loc));
            values.Add(SettingsModel.Value(_state.View[i], _settings, loc));
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
        Math.Max(3, Math.Min(SettingsModel.Count, screenH - 10));

    /// <summary>Окно списка неизвестно без размеров экрана — оценка для клавиш (Draw доклампит).</summary>
    private static int MaxListFallback() => SettingsModel.Count;

    /// <summary>Применить выбранную строку (следующее значение) и остаться открытым.</summary>
    private void Activate()
    {
        if (_state.View.Count == 0)
            return;
        _state.MoveTo(_state.Selected, MaxListFallback());
        int row = _state.View[_state.Selected];
        SettingsModel.Cycle(_settings, row, 1);
        _store.Save(_settings);
        _onChanged();
        if (_loc is not null)
        {
            _state.Refilter(_settings, _loc);
            int at = _state.View.IndexOf(row);
            _state.MoveTo(at >= 0 ? at : 0, MaxListFallback());
        }
    }
}
