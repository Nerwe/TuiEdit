namespace TuiEdit;

/// <summary>Прямоугольник окна: левый верхний угол + размер.</summary>
public readonly record struct DialogBox(int X0, int Y0, int W, int H);

/// <summary>
/// Базовый класс диалогового окна: центрирование, рамка с заголовком,
/// цикл ввода с фокус-ловушкой. Контент, клавиши и исход — в наследниках
/// (ModalDialog, FileDialog, SettingsDialog); общая логика — здесь, чтобы
/// не дублировать её в каждом окне заново. Расширение — наследованием.
/// </summary>
internal abstract class Dialog
{
    /// <summary>Диалог закрыт — цикл Render/Read завершается.</summary>
    public bool Closed { get; protected set; }

    /// <summary>Заголовок окна.</summary>
    protected abstract string GetTitle(Loc loc);

    /// <summary>
    /// Рамка по центру экрана под контент. null — не влезает (не рисуем).
    /// Каждый наследник повторяет свою прежнюю формулу размера 1 в 1.
    /// </summary>
    protected abstract DialogBox? Measure(int screenW, int screenH, Loc loc);

    /// <summary>Контент поверх пустой рамки (внутренняя ширина — box.W - 2).</summary>
    protected abstract void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box);

    /// <summary>Цвета рамки (обычно модальные).</summary>
    protected virtual (Rgb fg, Rgb bg) FrameColors(Theme theme) => (theme.ModalFg, theme.ModalBg);

    /// <summary>Отрисовать поверх кадра: рамка + контент наследника.</summary>
    public void Draw(Screen screen, Theme theme, Loc loc)
    {
        DialogBox? box = Measure(screen.Width, screen.Height, loc);
        if (box is null)
            return;
        var (fg, bg) = FrameColors(theme);
        DrawFrame(screen, GetTitle(loc), fg, bg, box.Value);
        DrawContent(screen, theme, loc, fg, bg, box.Value);
    }

    private static void DrawFrame(Screen screen, string title, Rgb fg, Rgb bg, DialogBox box)
    {
        screen.Text(box.X0, box.Y0, Screen.TitleRow(title, box.W), fg, bg);
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

    /// <summary>Центрировать текст (обрезается до ширины).</summary>
    internal static string CenterPad(string s, int width)
    {
        if (s.Length >= width)
            return s[..Math.Max(0, width)];
        int left = (width - s.Length) / 2;
        return new string(' ', left) + s + new string(' ', width - s.Length - left);
    }

    /// <summary>Усечь строку серединой (обрезается до ширины).</summary>
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
