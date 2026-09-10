namespace TuiEdit;

/// <summary>Provides help as a dialog window: section rows reuse the same help.k* keys as console --help.</summary>
internal sealed class HelpDialog : Dialog
{
    private readonly List<(string title, List<string> rows)> _sections;
    private readonly string _title;
    private readonly string _hint;
    private readonly int _total;
    private int _scroll;
    private int _visCount = 10;

    public HelpDialog(Loc loc)
    {
        _title = loc["help.dlg.title"];
        _hint = loc["help.dlg.hint"];
        _sections = new List<(string, List<string>)>
        {
            (loc["help.sec.file"], new List<string> { loc["help.k1"] }),
            (loc["help.sec.find"], new List<string> { loc["help.k2"] }),
            (loc["help.sec.edit"], new List<string> { loc["help.k3"], loc["help.k4"], loc["help.k5"], loc["help.k8"] }),
            (loc["help.sec.nav"], new List<string> { loc["help.k6"], loc["help.k7"] }),
            (loc["help.sec.menu"], new List<string> { loc["help.k9"] }),
            (loc["help.sec.view"], new List<string> { loc["help.k11"], loc["help.k13"], loc["help.k14"], loc["help.k15"], loc["help.k16"], loc["help.k18"] }),
            (loc["help.sec.manager"], new List<string> { loc["help.k10"], loc["help.k17"], loc["help.k12"] }),
        };
        _total = _sections.Sum(s => 1 + s.rows.Count);
    }

    public int TotalRows => _total;

    public int Scroll => _scroll;

    protected override string GetTitle(Loc loc) => _title;

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        int content = _title.Length + 2;
        foreach (var s in _sections)
        {
            content = Math.Max(content, s.title.Length + 4);
            foreach (string row in s.rows)
                content = Math.Max(content, SpanWidth(row));
        }
        content = Math.Max(content, SpanWidth(_hint));
        int boxW = Math.Min(Math.Max(content + 6, 24), screenW);
        if (boxW < 12)
            return null;
        int boxH = Math.Min(_total + 3, screenH);
        if (boxH < 5)
            return null;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = TopY(screenH, boxH);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    /// <summary>Computes how many content rows are visible (for scrolling and tests).</summary>
    public static int VisibleRows(int boxH) => Math.Max(1, boxH - 3);

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        int inner = box.W - 2;
        int visCount = VisibleRows(box.H);
        _visCount = visCount;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _total - visCount));
        var flat = new List<(bool header, string text)>();
        foreach (var s in _sections)
        {
            flat.Add((true, s.title));
            foreach (string row in s.rows)
                flat.Add((false, row));
        }
        for (int vi = 0; vi < visCount && _scroll + vi < flat.Count; vi++)
        {
            var (header, text) = flat[_scroll + vi];
            int y = box.Y0 + 1 + vi;
            if (header)
            {
                // Section header: dim rule, header itself in accent.
                string rule = "── " + text + " " + new string('─', Math.Max(0, inner - text.Length - 5));
                rule = rule[..Math.Min(rule.Length, inner)];
                screen.Text(box.X0, y, "│", fg, bg);
                screen.Text(box.X0 + 1, y, rule, theme.ModalHintFg, bg);
                screen.Text(box.X0 + 1 + 3, y,
                    text[..Math.Max(0, Math.Min(text.Length, inner - 4))], theme.AccentFg, bg);
                screen.Text(box.X0 + box.W - 1, y, "│", fg, bg);
            }
            else
            {
                screen.Text(box.X0, y, "│", fg, bg);
                WriteSpans(screen, box.X0 + 1, y, " " + text, fg, theme.AccentFg, bg, inner);
                screen.Text(box.X0 + box.W - 1, y, "│", fg, bg);
            }
        }
        Rgb hintFg = theme.ModalHintFg;
        int hintX = box.X0 + 1 + Math.Max(0, (inner - Math.Min(SpanWidth(_hint), inner)) / 2);
        screen.Text(box.X0, box.Y0 + box.H - 2, "│" + new string(' ', inner) + "│", hintFg, bg);
        WriteSpans(screen, hintX, box.Y0 + box.H - 2, _hint, hintFg, theme.AccentFg, bg,
            inner - (hintX - box.X0 - 1));
        screen.Text(box.X0, box.Y0 + box.H - 1, "└" + new string('─', inner) + "┘", fg, bg);
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return; // Swallows Ctrl/Alt, keeps help open
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F1:
            case ConsoleKey.Enter:
                Closed = true;
                return;
            case ConsoleKey.UpArrow: _scroll--; return;
            case ConsoleKey.DownArrow: _scroll++; return;
            case ConsoleKey.Home: _scroll = 0; return;
            case ConsoleKey.End: _scroll = int.MaxValue; return;
            case ConsoleKey.PageUp: _scroll -= Math.Max(1, _visCount); return;
            case ConsoleKey.PageDown: _scroll += Math.Max(1, _visCount); return;
        }
    }
}
