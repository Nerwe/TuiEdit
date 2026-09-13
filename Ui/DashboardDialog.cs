namespace TuiEdit;

/// <summary>
/// Startup dashboard: version, recent files, hints. Plain launches only
/// (no CLI files, session, or drafts); digits/Enter open a recent file,
/// Esc dismisses, other printable keys type through into the fresh buffer.
/// </summary>
internal sealed class DashboardDialog : Dialog
{
    private readonly string _version;
    private readonly List<string> _recent;
    private readonly Action<string> _onPick;
    private int _selected;
    private int _top;
    private int _screenW = 80;
    private int _screenH = 24;

    /// <summary>Picked path (null means dismissed without picking).</summary>
    public string? Picked { get; private set; }

    /// <summary>Printable key that dismissed the dashboard (re-injected by the editor).</summary>
    public KeyInput? DismissKey { get; private set; }

    public DashboardDialog(string version, IReadOnlyList<string> recent, Action<string> onPick)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(onPick);
        _version = version;
        _recent = new List<string>(recent);
        _onPick = onPick;
    }

    protected override string GetTitle(Loc loc) => loc["dashboard.title"];

    private static int FixedRows => 1 + 1 + 1; // version, section rule, hint

    private int ShownRows(int screenH) =>
        Math.Max(0, Math.Min(_recent.Count, screenH - 2 - FixedRows));

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        _screenW = screenW;
        _screenH = screenH;
        string header = _recent.Count == 0 ? loc["dashboard.empty"] : loc["dashboard.recent"];
        int inner = GetTitle(loc).Length + 2;
        inner = Math.Max(inner, SpanWidth(loc.Format("dashboard.version", _version)));
        inner = Math.Max(inner, header.Length + 6);
        foreach (string path in _recent)
            inner = Math.Max(inner, Math.Min(path.Length + 5, screenW - 2));
        inner = Math.Max(inner, SpanWidth(loc["dashboard.hint"]));
        int boxW = Math.Min(Math.Max(inner + 6, 24), screenW);
        if (boxW < 12)
            return null;
        int boxH = FixedRows + ShownRows(screenH) + 2;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = TopY(screenH, boxH);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        int inner = box.W - 2;
        int y = box.Y0 + 1;
        screen.Text(box.X0, y, "│" + CenterPad(loc.Format("dashboard.version", _version), inner) + "│", fg, bg);
        y++;
        string header = _recent.Count == 0 ? loc["dashboard.empty"] : loc["dashboard.recent"];
        string rule = "── " + header + " " + new string('─', Math.Max(0, inner - header.Length - 5));
        rule = rule[..Math.Min(rule.Length, inner)];
        screen.Text(box.X0, y, "│", fg, bg);
        screen.Text(box.X0 + 1, y, rule, theme.ModalHintFg, bg);
        screen.Text(box.X0 + 1 + 3, y,
            header[..Math.Max(0, Math.Min(header.Length, inner - 4))], theme.AccentFg, bg);
        screen.Text(box.X0 + box.W - 1, y, "│", fg, bg);
        y++;
        int shown = ShownRows(_screenH);
        _top = Math.Clamp(_top, 0, Math.Max(0, _recent.Count - Math.Max(1, shown)));
        _selected = _recent.Count == 0 ? 0 : Math.Clamp(_selected, 0, _recent.Count - 1);
        for (int vi = 0; vi < shown; vi++)
        {
            int i = _top + vi;
            string label = $" {(i + 1) % 10}  {MiddleTruncate(_recent[i], Math.Max(0, inner - 5))}";
            string cell = FitCell(label, inner);
            if (i == _selected)
                screen.Text(box.X0, y, "│" + cell + "│", theme.ButtonSelFg, theme.ButtonSelBg);
            else
                screen.Text(box.X0, y, "│" + cell + "│", fg, bg);
            y++;
        }
        string hint = loc["dashboard.hint"];
        int visLen = Math.Min(SpanWidth(hint), inner);
        int hx = box.X0 + 1 + Math.Max(0, (inner - visLen) / 2);
        screen.Text(box.X0, y, "│" + new string(' ', inner) + "│", theme.ModalHintFg, bg);
        WriteSpans(screen, hx, y, hint, theme.ModalHintFg, theme.AccentFg, bg, inner - (hx - box.X0 - 1));
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Closed = true;
                return;
            case ConsoleKey.Enter:
                if (_recent.Count > 0)
                    Pick(Math.Clamp(_selected, 0, _recent.Count - 1));
                else
                    Closed = true;
                return;
            case ConsoleKey.UpArrow:
                if (_recent.Count > 0)
                    _selected = (_selected - 1 + _recent.Count) % _recent.Count;
                FollowSelection();
                return;
            case ConsoleKey.DownArrow:
                if (_recent.Count > 0)
                    _selected = (_selected + 1) % _recent.Count;
                FollowSelection();
                return;
            case ConsoleKey.Home:
                _selected = 0;
                FollowSelection();
                return;
            case ConsoleKey.End:
                if (_recent.Count > 0)
                    _selected = _recent.Count - 1;
                FollowSelection();
                return;
        }
        if (!ctrl && !alt && key.Key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
        {
            int n = key.Key == ConsoleKey.D0 ? 10 : (int)key.Key - (int)ConsoleKey.D0;
            if (n >= 1 && n <= _recent.Count)
                Pick(n - 1);
            else
                Closed = true;
            return;
        }
        if (!ctrl && !alt && !char.IsControl(key.KeyChar) && key.KeyChar != '\0')
        {
            DismissKey = new KeyInput(key); // printable types through into the editor
            Closed = true;
            return;
        }
        if (key.Key is ConsoleKey.Tab or ConsoleKey.Spacebar && !ctrl && !alt)
        {
            Closed = true;
        }
        // Otherwise swallowed (focus trap).
    }

    public override bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return false;
        DialogBox db = box.Value;
        if (x < db.X0 || x >= db.X0 + db.W || y < db.Y0 || y >= db.Y0 + db.H)
            return false;
        int shown = ShownRows(screenH);
        int firstRow = db.Y0 + 1 + 1 + 1; // version, rule, then rows
        int row = y - firstRow;
        if (row >= 0 && row < shown)
            Pick(_top + row);
        else
            Closed = true;
        return true;
    }

    /// <summary>Keeps the selected row inside the visible window.</summary>
    private void FollowSelection()
    {
        int shown = Math.Max(1, ShownRows(_screenH));
        if (_selected < _top)
            _top = _selected;
        else if (_selected >= _top + shown)
            _top = _selected - shown + 1;
    }

    private void Pick(int index)
    {
        Picked = _recent[Math.Clamp(index, 0, _recent.Count - 1)];
        Closed = true;
        _onPick(Picked);
    }
}
