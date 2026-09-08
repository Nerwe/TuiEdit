namespace TuiEdit;

/// <summary>Модальный попап поверх редактора: исход (кнопка/отмена) — колбэком в редактор, где живут pending-действия.</summary>
internal sealed class ModalDialog : Dialog
{
    private readonly ModalState _state;
    private readonly Action<ModalState, ModalKeyOutcome> _onDone;

    public ModalDialog(ModalState state, Action<ModalState, ModalKeyOutcome> onDone)
    {
        _state = state;
        _onDone = onDone;
    }

    protected override string GetTitle(Loc loc) => _state.Title;

    protected override (Rgb fg, Rgb bg) FrameColors(Theme theme) =>
        _state.Danger ? (theme.ModalDangerFg, theme.ModalDangerBg) : base.FrameColors(theme);

    protected override Rgb TitleFg(Theme theme) =>
        _state.Danger ? theme.ModalDangerFg : theme.AccentFg;

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        bool vertical = _state.Kind is ModalKind.Recent or ModalKind.Restore or ModalKind.Tabs or ModalKind.Complete or ModalKind.Grep;
        int btnWidth = _state.Buttons.Count == 0 ? 0 : vertical
            ? _state.Buttons.Max(b => b.Label.Length)
            : _state.Buttons.Sum(b => b.Label.Length + 4) + (_state.Buttons.Count - 1) * 2;
        int content = _state.Title.Length + 2;
        foreach (string line in _state.Lines)
            content = Math.Max(content, line.Length);
        content = Math.Max(content, btnWidth);
        content = Math.Max(content, SpanWidth(_state.Hint));
        int boxW = Math.Min(Math.Max(content + 6, 24), screenW);
        if (boxW < 12)
            return null;
        // Строки: заголовок + текст + кнопки (у горизонтальных + разделитель) + хинт + рамка.
        int btnRows = vertical ? Math.Min(_state.MaxVisibleButtons, _state.Buttons.Count) : 2;
        int hintRows = _state.Hint.Length > 0 ? 1 : 0;
        int boxH = 1 + _state.Lines.Count + btnRows + hintRows + 1;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = Math.Max(0, (screenH - boxH) / 2);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        int x0 = box.X0, y0 = box.Y0, boxW = box.W;
        ModalState m = _state;
        for (int i = 0; i < m.Lines.Count; i++)
            screen.Text(x0, y0 + 1 + i, "│" + CenterPad(m.Lines[i], boxW - 2) + "│", fg, bg);
        int bottomY;
        int btnY = 0;
        if (m.Kind is ModalKind.Recent or ModalKind.Restore or ModalKind.Tabs or ModalKind.Complete or ModalKind.Grep)
        {
            int visCount = Math.Min(m.MaxVisibleButtons, m.Buttons.Count - m.ButtonTop);
            for (int vi = 0; vi < visCount; vi++)
            {
                int i = m.ButtonTop + vi;
                string label = m.Buttons[i].Label;
                if (vi == 0 && m.ButtonTop > 0)
                    label += " ↑";
                if (vi == visCount - 1 && m.ButtonTop + visCount < m.Buttons.Count)
                    label += " ↓";
                int by = y0 + 1 + m.Lines.Count + vi;
                string cell = (" " + label).PadRight(boxW - 2)[..(boxW - 2)];
                int eff = HoverActive ? HoverButton ?? m.Selected : m.Selected;
                if (i == eff)
                    screen.Text(x0, by, "│" + cell + "│", theme.ButtonSelFg, theme.ButtonSelBg);
                else
                    screen.Text(x0, by, "│" + cell + "│", fg, bg);
            }
            bottomY = y0 + 1 + m.Lines.Count + visCount;
        }
        else
        {
            screen.Text(x0, y0 + 1 + m.Lines.Count, "│" + new string(' ', boxW - 2) + "│", fg, bg);
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
            btnY = y0 + 2 + m.Lines.Count;
            screen.Text(x0, btnY, btnRow.ToString(), fg, bg);
            int bx = x0 + 1 + padLeft;
            for (int i = 0; i < m.Buttons.Count; i++)
            {
                int eff = HoverActive ? HoverButton ?? m.Selected : m.Selected;
                if (i == eff)
                    screen.Text(bx, btnY, cells[i], theme.ButtonSelFg, theme.ButtonSelBg);
                bx += cells[i].Length + 4;
            }
            bottomY = btnY + 1;
        }
        if (m.Hint.Length > 0)
        {
            Rgb hintFg = m.Danger ? theme.ModalHintDangerFg : theme.ModalHintFg;
            int inner = boxW - 2;
            int visLen = Math.Min(SpanWidth(m.Hint), inner);
            int hx = x0 + 1 + Math.Max(0, (inner - visLen) / 2);
            screen.Text(x0, bottomY, "│" + new string(' ', inner) + "│", hintFg, bg);
            WriteSpans(screen, hx, bottomY, m.Hint, hintFg, theme.AccentFg, bg, inner - (hx - x0 - 1));
            bottomY++;
        }
        screen.Text(x0, bottomY, "└" + new string('─', boxW - 2) + "┘", fg, bg);
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        ModalKeyOutcome o = _state.HandleKey(key);
        if (!o.Done)
            return;
        Closed = true;
        _onDone(_state, o);
    }

    /// <summary>Кнопка под hover (рисуется как выбранная); null — нет.</summary>
    public int? HoverButton { get; set; }

    /// <summary>Показывать ли hover (мышь была последним вводом).</summary>
    public bool HoverActive { get; set; }

    /// <summary>
    /// Клик: кнопка — нажать, внутри бокса мимо кнопок — проглотить,
    /// снаружи — false (редактор игнорит, модалка не закрывается).
    /// Раскладка — зеркало Measure/DrawContent.
    /// </summary>
    public bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        int? hit = HitButton(x, y, screenW, screenH, loc);
        if (hit is null)
        {
            // Внутри бокса мимо кнопок — глушим (фокус-ловушка); снаружи — false.
            DialogBox? box = Measure(screenW, screenH, loc);
            if (box is null)
                return false;
            DialogBox db = box.Value;
            return x >= db.X0 && x < db.X0 + db.W && y >= db.Y0 && y < db.Y0 + db.H;
        }
        Closed = true;
        _onDone(_state, ModalKeyOutcome.Press(hit.Value));
        return true;
    }

    /// <summary>Индекс кнопки под координатами или null. Чистая — для hover и тестов.</summary>
    public int? HitButton(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return null;
        DialogBox db = box.Value;
        int x0 = db.X0, y0 = db.Y0, boxW = db.W;
        if (x < x0 || x >= x0 + boxW || y < y0 || y >= y0 + db.H)
            return null;
        ModalState m = _state;
        if (IsListKind(m.Kind))
        {
            int visCount = Math.Min(m.MaxVisibleButtons, m.Buttons.Count - m.ButtonTop);
            int row = y - (y0 + 1 + m.Lines.Count);
            if (row >= 0 && row < visCount)
                return m.ButtonTop + row;
            return null;
        }
        if (y != y0 + 2 + m.Lines.Count)
            return null;
        int used = 0;
        foreach (ModalButton b in m.Buttons)
            used += b.Label.Length + 4;
        used -= 4;
        int padLeft = Math.Max(2, (boxW - 2 - used) / 2);
        int bx = x0 + 1 + padLeft;
        for (int i = 0; i < m.Buttons.Count; i++)
        {
            if (x >= bx && x < bx + m.Buttons[i].Label.Length)
                return i;
            bx += m.Buttons[i].Label.Length + 4;
        }
        return null;
    }

    /// <summary>Колесо над списком — стрелки (выбор+скролл); горизонтальным — мимо.</summary>
    public void ScrollList(int dir)
    {
        if (!IsListKind(_state.Kind))
            return;
        ConsoleKeyInfo k = new('\0', dir < 0 ? ConsoleKey.UpArrow : ConsoleKey.DownArrow, false, false, false);
        ModalKeyOutcome o = _state.HandleKey(k);
        if (o.Done)
        {
            Closed = true;
            _onDone(_state, o);
        }
    }

    private static bool IsListKind(ModalKind kind) =>
        kind is ModalKind.Recent or ModalKind.Restore or ModalKind.Tabs or ModalKind.Complete or ModalKind.Grep;
}
