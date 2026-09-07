namespace TuiEdit;

/// <summary>
/// Модальный попап поверх редактора: состояние — <see cref="ModalState"/>,
/// исход (кнопка/отмена) — колбэком в редактор, где живут pending-действия.
/// Рамка, центрирование и цикл — из базового <see cref="Dialog"/>.
/// </summary>
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

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        bool vertical = _state.Kind is ModalKind.Recent or ModalKind.Restore;
        int btnWidth = _state.Buttons.Count == 0 ? 0 : vertical
            ? _state.Buttons.Max(b => b.Label.Length)
            : _state.Buttons.Sum(b => b.Label.Length + 4) + (_state.Buttons.Count - 1) * 2;
        int content = _state.Title.Length + 2;
        foreach (string line in _state.Lines)
            content = Math.Max(content, line.Length);
        content = Math.Max(content, btnWidth);
        content = Math.Max(content, _state.Hint.Length);
        int boxW = Math.Min(Math.Max(content + 6, 24), screenW);
        if (boxW < 12)
            return null;
        int boxH = _state.Lines.Count + (vertical ? Math.Min(_state.MaxVisibleButtons, _state.Buttons.Count) : 1)
            + (_state.Hint.Length > 0 ? 3 : 2);
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
        if (m.Kind is ModalKind.Recent or ModalKind.Restore)
        {
            // Кнопки списком слева, выбранная подсвечена целиком;
            // длинный список — срез со стрелками скролла.
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
                if (i == m.Selected)
                    screen.Text(x0, by, "│" + cell + "│", theme.ButtonSelFg, theme.ButtonSelBg);
                else
                    screen.Text(x0, by, "│" + cell + "│", fg, bg);
            }
            bottomY = y0 + 1 + m.Lines.Count + visCount;
        }
        else
        {
            // Кнопки по центру (хоткеи уже в названиях, напр. [Y]).
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
            // Подсветка выбранной кнопки поверх.
            int bx = x0 + 1 + padLeft;
            for (int i = 0; i < m.Buttons.Count; i++)
            {
                if (i == m.Selected)
                    screen.Text(bx, btnY, cells[i], theme.ButtonSelFg, theme.ButtonSelBg);
                bx += cells[i].Length + 4;
            }
            bottomY = btnY + 1;
        }
        // Хинт (если есть) и низ.
        if (m.Hint.Length > 0)
        {
            Rgb hintFg = m.Danger ? theme.ModalHintDangerFg : theme.ModalHintFg;
            screen.Text(x0, bottomY, "│" + CenterPad(m.Hint, boxW - 2) + "│", hintFg, bg);
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
}
