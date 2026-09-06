namespace TuiEdit;

/// <summary>Тип модального попапа (влияет на цвета).</summary>
public enum ModalKind
{
    /// <summary>Несохранённые изменения / подтверждение (красный, как в MS Edit).</summary>
    UnsavedQuit,
    /// <summary>О программе (синий).</summary>
    About,
    /// <summary>Ошибка (красный).</summary>
    Error,
    /// <summary>Перезапись файла (красный).</summary>
    Overwrite,
}

/// <summary>Кнопка попапа: подпись и хоткей-буква (без Enter). '\0' — без хоткея.</summary>
public sealed record ModalButton(string Label, char Hotkey);

/// <summary>Исход обработки клавиши попапом.</summary>
public readonly record struct ModalKeyOutcome(bool Done, bool Cancelled, int Button)
{
    /// <summary>Попап остаётся открыт (клавиша проглочена).</summary>
    public static readonly ModalKeyOutcome Open = new(false, false, -1);

    /// <summary>Закрыт через Esc.</summary>
    public static ModalKeyOutcome Cancel() => new(true, true, -1);

    /// <summary>Нажата кнопка с индексом.</summary>
    public static ModalKeyOutcome Press(int index) => new(true, false, index);
}

/// <summary>
/// Центрированный модальный попап в стиле MS Edit
/// (<c>modal_begin/modal_end</c> в <c>tui.rs</c>, диалоги в <c>draw_editor.rs</c>,
/// <c>main.rs</c>, <c>state.rs</c> репозитория microsoft/edit):
/// рамка с заголовком, фокус-ловушка (весь ввод глотается),
/// Esc/мимо — отмена, кнопки — Enter, стрелки и одноклавишные хоткеи.
/// Чистая модель без консоли — покрывается unit-тестами.
/// </summary>
public sealed class ModalState
{
    /// <summary>Тип попапа.</summary>
    public ModalKind Kind { get; }

    /// <summary>Заголовок на верхней рамке.</summary>
    public string Title { get; }

    /// <summary>Строки текста.</summary>
    public List<string> Lines { get; }

    /// <summary>Кнопки слева направо.</summary>
    public List<ModalButton> Buttons { get; }

    /// <summary>Индекс подсвеченной кнопки.</summary>
    public int Selected { get; private set; }

    /// <summary>Красная (тревожная) расцветка.</summary>
    public bool Danger => Kind is ModalKind.UnsavedQuit or ModalKind.Error or ModalKind.Overwrite;

    /// <summary>Строка-подсказка клавиш внутри попапа.</summary>
    public string Hint { get; }

    private ModalState(ModalKind kind, string title, List<string> lines, List<ModalButton> buttons, int selected, string hint)
    {
        Kind = kind;
        Title = title;
        Lines = lines;
        Buttons = buttons;
        Selected = Math.Clamp(selected, 0, buttons.Count - 1);
        Hint = hint;
    }

    /// <summary>Попап «несохранённые изменения»: кнопки с хоткеями в скобках, без хинтов.</summary>
    public static ModalState UnsavedQuit(Loc loc) => new(
        ModalKind.UnsavedQuit,
        loc["modal.unsaved.title"],
        new List<string> { loc["modal.unsaved.desc"] },
        new List<ModalButton>
        {
            new(loc["modal.unsaved.save"], 'Y'),
            new(loc["modal.unsaved.discard"], 'N'),
            new(loc["modal.unsaved.cancel"], '\0'),
        },
        selected: 0,
        hint: string.Empty);

    /// <summary>Попап «о программе».</summary>
    public static ModalState About(Loc loc, string version) => new(
        ModalKind.About,
        loc["modal.about.title"],
        new List<string>
        {
            loc.Format("modal.about.l1", version),
            loc["modal.about.l2"],
            loc["modal.about.l3"],
        },
        new List<ModalButton> { new(loc["modal.about.ok"], '\0') },
        selected: 0,
        loc["modal.close.hint"]);

    /// <summary>Попап ошибки (заголовок и текст уже локализованы вызывающим).</summary>
    public static ModalState Error(Loc loc, string title, string message) => new(
        ModalKind.Error,
        title,
        message.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Take(6).ToList(),
        new List<ModalButton> { new(loc["modal.error.ok"], '\0') },
        selected: 0,
        loc["modal.close.hint"]);

    /// <summary>Попап перезаписи: Да [Y] / Нет, без хинтов.</summary>
    public static ModalState Overwrite(Loc loc, string fileName) => new(
        ModalKind.Overwrite,
        loc["picker.ow.title"],
        new List<string> { loc["picker.ow.exists"], fileName },
        new List<ModalButton>
        {
            new(loc["picker.ow.yes"], 'Y'),
            new(loc["picker.ow.no"], 'N'),
        },
        selected: 0,
        hint: string.Empty);

    /// <summary>
    /// Обрабатывает клавишу: стрелки/Home/End двигают подсветку,
    /// Enter нажимает подсвеченную кнопку, Esc — отмена,
    /// буква-хоткей нажимает кнопку сразу. Остальное глотается.
    /// </summary>
    /// <param name="key">Нажатие клавиши.</param>
    /// <returns>Исход: попап открыт, отменён или нажата кнопка.</returns>
    public ModalKeyOutcome HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
            return ModalKeyOutcome.Cancel();

        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return ModalKeyOutcome.Open; // Ctrl/Alt глотаем, попап не закрываем

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                Selected = (Selected - 1 + Buttons.Count) % Buttons.Count;
                return ModalKeyOutcome.Open;
            case ConsoleKey.RightArrow:
                Selected = (Selected + 1) % Buttons.Count;
                return ModalKeyOutcome.Open;
            case ConsoleKey.Home:
                Selected = 0;
                return ModalKeyOutcome.Open;
            case ConsoleKey.End:
                Selected = Buttons.Count - 1;
                return ModalKeyOutcome.Open;
            case ConsoleKey.Enter:
                return ModalKeyOutcome.Press(Selected);
        }

        char c = char.ToUpperInvariant(key.KeyChar);
        if (c == '\0' || char.IsControl(c))
            return ModalKeyOutcome.Open;

        for (int i = 0; i < Buttons.Count; i++)
            if (MatchesHotkey(c, Buttons[i].Hotkey))
                return ModalKeyOutcome.Press(i);

        return ModalKeyOutcome.Open;
    }

    private static bool MatchesHotkey(char pressed, char hotkey) => char.ToUpperInvariant(hotkey) switch
    {
        '\0' => false,
        // Русская раскладка: S→Ы, N→Т, Y→Н.
        'S' => pressed is 'S' or 'Ы',
        'N' => pressed is 'N' or 'Т',
        'Y' => pressed is 'Y' or 'Н',
        var h => pressed == h,
    };
}
