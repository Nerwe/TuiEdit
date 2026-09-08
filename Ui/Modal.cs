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
    /// <summary>Недавние файлы (синий, кнопки списком).</summary>
    Recent,
    /// <summary>Список вкладок (синий, кнопки списком).</summary>
    Tabs,
    /// <summary>Восстановление черновиков (синий, кнопки списком).</summary>
    Restore,
    /// <summary>Массовая замена (красный).</summary>
    ReplaceConfirm,
    /// <summary>Автодополнение (синий, кнопки списком).</summary>
    Complete,
    /// <summary>Результаты поиска по файлам (синий, кнопки списком).</summary>
    Grep,
}

/// <summary>Кнопка попапа: подпись и хоткей-буква (без Enter). '\0' — без хоткея.</summary>
public sealed record ModalButton(string Label, char Hotkey);

public readonly record struct ModalKeyOutcome(bool Done, bool Cancelled, int Button)
{
    /// <summary>Попап остаётся открыт (клавиша проглочена).</summary>
    public static readonly ModalKeyOutcome Open = new(false, false, -1);

    /// <summary>Закрыт через Esc.</summary>
    public static ModalKeyOutcome Cancel() => new(true, true, -1);

    public static ModalKeyOutcome Press(int index) => new(true, false, index);
}

/// <summary>Центрированный модальный попап в стиле MS Edit (<c>modal_begin/modal_end</c> в <c>tui.rs</c>): рамка с заголовком, фокус-ловушка (весь ввод глотается), Esc — отмена, кнопки — Enter, стрелки и одноклавишные хоткеи.</summary>
public sealed class ModalState
{
    public ModalKind Kind { get; }

    public string Title { get; }

    public List<string> Lines { get; }

    /// <summary>Кнопки слева направо.</summary>
    public List<ModalButton> Buttons { get; }

    public int Selected { get; private set; }

    /// <summary>Первая видимая кнопка (скролл вертикального списка).</summary>
    public int ButtonTop { get; private set; }

    /// <summary>Сколько кнопок видно разом (скролл вертикального списка).</summary>
    public int MaxVisibleButtons { get; }

    public bool Danger => Kind is ModalKind.UnsavedQuit or ModalKind.Error or ModalKind.Overwrite or ModalKind.ReplaceConfirm;

    public string Hint { get; }

    private ModalState(ModalKind kind, string title, List<string> lines, List<ModalButton> buttons, int selected, string hint, int maxVisibleButtons = int.MaxValue)
    {
        Kind = kind;
        Title = title;
        Lines = lines;
        Buttons = buttons;
        Selected = Math.Clamp(selected, 0, buttons.Count - 1);
        Hint = hint;
        MaxVisibleButtons = Math.Max(1, maxVisibleButtons);
        EnsureButtonVisible();
    }

    private void EnsureButtonVisible()
    {
        if (Selected < ButtonTop)
            ButtonTop = Selected;
        else if (Selected >= ButtonTop + MaxVisibleButtons)
            ButtonTop = Selected - MaxVisibleButtons + 1;
    }

    /// <summary>Попап «несохранённые изменения»: кнопки с хоткеями в скобках, без хинтов.</summary>
    public static ModalState UnsavedQuit(Loc loc, string? file) => new(
        ModalKind.UnsavedQuit,
        loc["modal.unsaved.title"],
        new List<string> { file is null ? loc["modal.unsaved.desc"] : loc.Format("modal.unsaved.descfile", file) },
        new List<ModalButton>
        {
            new(loc["modal.unsaved.save"], 'Y'),
            new(loc["modal.unsaved.discard"], 'N'),
            new(loc["modal.unsaved.cancel"], '\0'),
        },
        selected: 0,
        hint: string.Empty);

    public static ModalState About(Loc loc, string version) => new(
        ModalKind.About,
        loc["modal.about.title"],
        new List<string>
        {
            loc.Format("modal.about.l1", version),
            loc["modal.about.l2"],
            loc["modal.about.date"],
            loc["modal.about.author"],
            loc["modal.about.license"],
        },
        new List<ModalButton> { new(loc["modal.about.ok"], '\0') },
        selected: 0,
        hint: string.Empty);

    /// <summary>Попап ошибки (заголовок и текст уже локализованы вызывающим).</summary>
    public static ModalState Error(Loc loc, string title, string message) => new(
        ModalKind.Error,
        title,
        message.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Take(6).ToList(),
        new List<ModalButton> { new(loc["modal.error.ok"], '\0') },
        selected: 0,
        loc["modal.close.hint"]);

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

    public static ModalState ConfirmReplace(Loc loc, string term, int count) => new(
        ModalKind.ReplaceConfirm,
        loc["modal.replace.title"],
        new List<string> { loc.Format("modal.replace.desc", count, term) },
        new List<ModalButton>
        {
            new(loc["picker.ow.yes"], 'Y'),
            new(loc["picker.ow.no"], 'N'),
        },
        selected: 0,
        hint: string.Empty);

    /// <summary>Попап автодополнения: слова-кандидаты списком; пустой список запрещён.</summary>
    public static ModalState Complete(Loc loc, List<string> words)
    {
        if (words.Count == 0)
            throw new ArgumentException("Нет вариантов.", nameof(words));
        return new(
            ModalKind.Complete,
            loc["modal.complete.title"],
            new List<string>(),
            words.Select((w, i) => new ModalButton(w, NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 10);
    }

    /// <summary>Попап результатов grep: строки файл:строка; пустой список запрещён.</summary>
    public static ModalState Grep(Loc loc, List<GrepHit> hits)
    {
        if (hits.Count == 0)
            throw new ArgumentException("Нет совпадений.", nameof(hits));
        return new(
            ModalKind.Grep,
            loc["modal.grep.title"],
            new List<string>(),
            hits.Select((h, i) => new ModalButton(
                $"{ShortGrepPath(h.File)}:{h.Row + 1}: {h.Text.Trim()}", NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 10);
    }

    private static string ShortGrepPath(string path) =>
        path.Length <= 40 ? path : "..." + path[^37..];

    /// <summary>Попап недавних файлов: каждый файл — кнопка-строка с хоткеем 1..9,0; видно разом 5, остальные — скроллом; пустой список запрещён.</summary>
    public static ModalState Recent(Loc loc, List<string> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("Нет файлов.", nameof(files));
        return new(
            ModalKind.Recent,
            loc["modal.recent.title"],
            new List<string>(),
            files.Select((f, i) => new ModalButton(NumberedLabel(i, ShortPath(f)), NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    private static string ShortPath(string path) =>
        path.Length <= 48 ? path : "..." + path[^45..];

    /// <summary>Хоткей кнопки по индексу: 1..9,0, дальше — без хоткея.</summary>
    private static char NumberHotkey(int i) => i < 9 ? (char)('1' + i) : i == 9 ? '0' : '\0';

    private static string NumberedLabel(int i, string text)
    {
        char hot = NumberHotkey(i);
        return hot == '\0' ? $"    {text}" : $"[{hot}] {text}";
    }

    /// <summary>Попап списка вкладок: каждая — кнопка-строка с хоткеем 1..9,0; видно разом 5, остальные — скроллом; пустой список запрещён.</summary>
    public static ModalState Tabs(Loc loc, List<string> titles)
    {
        if (titles.Count == 0)
            throw new ArgumentException("Нет вкладок.", nameof(titles));
        return new(
            ModalKind.Tabs,
            loc["modal.tabs.title"],
            new List<string>(),
            titles.Select((t, i) => new ModalButton(NumberedLabel(i, t), NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    /// <summary>Попап восстановления черновиков: каждый — кнопка-строка с хоткеем 1..9,0 и датой сохранения; видно разом 5, остальные — скроллом; пустой список запрещён.</summary>
    public static ModalState Restore(Loc loc, List<(string name, DateTime savedAt)> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("Нет черновиков.", nameof(items));
        return new(
            ModalKind.Restore,
            loc["modal.restore.title"],
            new List<string> { loc["modal.restore.desc"] },
            items.Select((it, i) => new ModalButton(
                NumberedLabel(i, $"{ShortPath(it.name)}  {it.savedAt.ToLocalTime():dd.MM HH:mm}"),
                NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    /// <summary>Обрабатывает клавишу: Enter — нажать подсвеченную, Esc — отмена, буква-хоткей — нажать сразу; остальное глотается (фокус-ловушка).</summary>
    public ModalKeyOutcome HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
            return ModalKeyOutcome.Cancel();

        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return ModalKeyOutcome.Open; // Ctrl/Alt глотаем, попап не закрываем

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
                Selected = (Selected - 1 + Buttons.Count) % Buttons.Count;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.RightArrow:
            case ConsoleKey.DownArrow:
                Selected = (Selected + 1) % Buttons.Count;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.Home:
                Selected = 0;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.End:
                Selected = Buttons.Count - 1;
                EnsureButtonVisible();
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
