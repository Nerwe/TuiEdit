namespace TuiEdit;

public readonly record struct DialogBox(int X0, int Y0, int W, int H);

/// <summary>Базовый класс диалогового окна: центрирование, рамка с заголовком, цикл ввода с фокус-ловушкой.</summary>
internal abstract class Dialog
{
    /// <summary>Диалог закрыт — цикл Render/Read завершается.</summary>
    public bool Closed { get; protected set; }

    protected abstract string GetTitle(Loc loc);

    /// <summary>Рамка по центру экрана под контент (null — не влезает); каждый наследник повторяет свою прежнюю формулу размера 1 в 1.</summary>
    protected abstract DialogBox? Measure(int screenW, int screenH, Loc loc);

    /// <summary>Контент поверх пустой рамки (внутренняя ширина — box.W - 2).</summary>
    protected abstract void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box);

    protected virtual (Rgb fg, Rgb bg) FrameColors(Theme theme) => (theme.ModalFg, theme.ModalBg);

    /// <summary>Цвет заголовка в рамке (по умолчанию акцент, как bold-title в opencode).</summary>
    protected virtual Rgb TitleFg(Theme theme) => theme.AccentFg;

    public void Draw(Screen screen, Theme theme, Loc loc)
    {
        DialogBox? box = Measure(screen.Width, screen.Height, loc);
        if (box is null)
            return;
        var (fg, bg) = FrameColors(theme);
        DrawFrame(screen, GetTitle(loc), fg, bg, box.Value, TitleFg(theme));
        DrawContent(screen, theme, loc, fg, bg, box.Value);
    }

    private static void DrawFrame(Screen screen, string title, Rgb fg, Rgb bg, DialogBox box, Rgb titleFg)
    {
        screen.Text(box.X0, box.Y0, Screen.TitleRow(title, box.W), fg, bg);
        int maxT = Math.Max(0, Math.Min(title.Length, box.W - 4));
        if (maxT > 0)
            screen.Text(box.X0 + 3, box.Y0, title[..maxT], titleFg, bg);
        for (int y = box.Y0 + 1; y < box.Y0 + box.H - 1; y++)
            screen.Text(box.X0, y, "│" + new string(' ', box.W - 2) + "│", fg, bg);
        screen.Text(box.X0, box.Y0 + box.H - 1, "└" + new string('─', box.W - 2) + "┘", fg, bg);
    }

    /// <summary>Обработать клавишу (фокус-ловушка: всё глотается).</summary>
    public abstract void HandleKey(ConsoleKeyInfo key);

    /// <summary>Вставка из буфера обмена (по умолчанию игнор).</summary>
    public virtual void Paste(string text)
    {
    }

    /// <summary>Отмена извне без исхода (напр. потеря консоли).</summary>
    public virtual void Cancel() => Closed = true;

    /// <summary>Позиция аппаратного курсора (null — спрятать).</summary>
    public virtual (int x, int y)? Cursor => null;

    /// <summary>Убрать разметку `акцента` (для консольного --help).</summary>
    internal static string StripSpans(string s) => s.Replace("`", "");

    /// <summary>Видимая длина строки со спанами.</summary>
    internal static int SpanWidth(string s)
    {
        int n = 0;
        foreach (char c in s)
            if (c != '`')
                n++;
        return n;
    }

    /// <summary>Строка с `акцентными` кусками (как bold-ключи в подвалах opencode); пишет до maxWidth видимых символов.</summary>
    internal static void WriteSpans(Screen screen, int x, int y, string text, Rgb fg, Rgb accentFg, Rgb bg, int maxWidth)
    {
        int cx = x, left = maxWidth;
        bool accent = false;
        foreach (char c in text)
        {
            if (c == '`')
            {
                accent = !accent;
                continue;
            }
            if (left <= 0)
                break;
            screen.Set(cx++, y, c, accent ? accentFg : fg, bg);
            left--;
        }
    }

    /// <summary>Замер окна опций: подписи слева, значения в ровной колонке.</summary>
    internal static DialogBox? MeasureOptions(int screenW, int screenH, string title, string[] labels, string[] values)
    {
        if (screenW < 20 || screenH < 5)
            return null;
        int labelW = 0, valW = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            labelW = Math.Max(labelW, labels[i].Length);
            valW = Math.Max(valW, values[i].Length);
        }
        int inner = 1 + 2 + labelW + 2 + (valW + 4) + 1;
        int boxW = Math.Min(Math.Max(inner + 2, title.Length + 6), screenW);
        int boxH = labels.Length + 2;
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = Math.Max(0, (screenH - boxH) / 2);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    /// <summary>Строки опций: маркер ● у выбранной, значения в одной колонке акцентным цветом.</summary>
    internal static void DrawOptionRows(Screen screen, Theme theme, DialogBox box, string[] labels, string[] values, int selected)
    {
        int labelW = 0;
        foreach (string l in labels)
            labelW = Math.Max(labelW, l.Length);
        int inner = box.W - 2;
        for (int i = 0; i < labels.Length; i++)
        {
            bool sel = i == selected;
            var (fg, bg) = sel
                ? (theme.ButtonSelFg, theme.ButtonSelBg)
                : (theme.ModalFg, theme.ModalBg);
            string val = $"< {values[i]} >";
            string cell = " " + (sel ? "● " : "  ") + labels[i].PadRight(labelW) + "  " + val;
            if (cell.Length > inner)
                cell = cell[..inner];
            cell = cell.PadRight(inner);
            int y = box.Y0 + 1 + i;
            screen.Text(box.X0, y, "│", fg, bg);
            // Значение — в ровной колонке акцентным (на выбранной строке — инверсия).
            int valX = 1 + 1 + 2 + labelW + 2;
            for (int k = 0; k < cell.Length && k < inner; k++)
            {
                bool isVal = k >= valX && k < valX + val.Length;
                screen.Set(box.X0 + 1 + k, y, cell[k],
                    sel ? fg : isVal ? theme.AccentFg : fg, bg);
            }
            screen.Text(box.X0 + box.W - 1, y, "│", fg, bg);
        }
    }

    /// <summary>Центрировать текст (обрезается до ширины).</summary>
    internal static string CenterPad(string s, int width)
    {
        if (s.Length >= width)
            return s[..Math.Max(0, width)];
        int left = (width - s.Length) / 2;
        return new string(' ', left) + s + new string(' ', width - s.Length - left);
    }

    internal static string MiddleTruncate(string s, int width)
    {
        if (s.Length <= width)
            return s;
        if (width <= 6)
            return s[..Math.Max(0, width)];
        int head = (width - 3) / 2;
        return s[..head] + "..." + s[^(width - 3 - head)..];
    }
}
